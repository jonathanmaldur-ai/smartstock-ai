using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Common;
using SmartStock.Application.Users;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Users;
using SmartStock.Infrastructure.Auth;
using SmartStock.Infrastructure.Identity;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Users;

internal sealed class UserAdminService(
    SmartStockDbContext db,
    UserManager<AppUser> userManager,
    SessionTokenService sessionTokens,
    PasswordLinkSender linkSender,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IOptions<AccountOptions> accountOptions,
    TimeProvider clock) : IUserAdminService
{
    private const int MaxNameLength = 150;

    private static readonly Error UserNotFound = Error.NotFound("user.not_found", "Usuário não encontrado.");

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Uma única consulta com o perfil de cada usuário (evita N+1).
        return await (
                from u in db.Users.AsNoTracking()
                join ur in db.UserRoles on u.Id equals ur.UserId into userRoles
                from ur in userRoles.DefaultIfEmpty()
                join r in db.Roles on ur.RoleId equals r.Id into roles
                from r in roles.DefaultIfEmpty()
                orderby u.FullName
                select new UserDto(
                    u.Id, u.FullName, u.Email!, r.Name ?? Roles.Consulta, u.IsActive,
                    u.PasswordHash != null, u.CreatedAt, u.LastLoginAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<CreatedUser>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var fullName = request.FullName.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        var validation = ValidateName(fullName) ?? ValidateEmail(email) ?? ValidateRole(request.Role);
        if (validation is not null)
            return validation;

        if (await userManager.FindByEmailAsync(email) is not null)
            return Error.Conflict("user.email_taken", $"O e-mail {email} já está cadastrado.");

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = fullName,
            IsActive = true,
            CreatedAt = clock.GetUtcNow()
        };

        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
            return Error.Validation("user.invalid", string.Join(" ", created.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(user, request.Role);
        var inviteSent = await linkSender.SendInviteAsync(user, cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.UserCreated, AuditResult.Success, "User", user.Id.ToString(),
            Details: new { email, fullName, role = request.Role, inviteSent }), cancellationToken);

        return new CreatedUser(await ToDtoAsync(user), inviteSent);
    }

    public async Task<Result<UserDto>> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var fullName = request.FullName.Trim();
        var validation = ValidateName(fullName) ?? ValidateRole(request.Role);
        if (validation is not null)
            return validation;

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return UserNotFound;

        var currentRole = await GetRoleAsync(user);
        var roleChanged = currentRole != request.Role;

        if (roleChanged && currentRole == Roles.Administrador)
        {
            var guard = await GuardAdminRemovalAsync(user, "alterar o seu próprio perfil de Administrador");
            if (guard is not null)
                return guard;
        }

        var before = new { fullName = user.FullName, role = currentRole };

        user.FullName = fullName;
        await userManager.UpdateAsync(user);

        if (roleChanged)
        {
            await userManager.RemoveFromRoleAsync(user, currentRole);
            await userManager.AddToRoleAsync(user, request.Role);
            // A troca de perfil vale a partir do próximo login: encerra as sessões atuais.
            await sessionTokens.RevokeAllAsync(user.Id, cancellationToken);
        }

        await audit.LogAsync(new AuditEntry(
            AuditActions.UserUpdated, AuditResult.Success, "User", user.Id.ToString(),
            Details: new { email = user.Email, before, after = new { fullName, role = request.Role } }), cancellationToken);

        return await ToDtoAsync(user);
    }

    public async Task<Result<UserDto>> SetActiveAsync(Guid userId, bool active, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return UserNotFound;

        if (user.IsActive == active)
            return await ToDtoAsync(user);

        if (!active)
        {
            var guard = await GuardAdminRemovalAsync(user, "desativar o seu próprio usuário");
            if (guard is not null)
                return guard;
        }

        user.IsActive = active;
        user.DeactivatedAt = active ? null : clock.GetUtcNow();
        await userManager.UpdateAsync(user);

        if (!active)
        {
            await userManager.UpdateSecurityStampAsync(user);
            await sessionTokens.RevokeAllAsync(user.Id, cancellationToken);
        }

        await audit.LogAsync(new AuditEntry(
            active ? AuditActions.UserActivated : AuditActions.UserDeactivated,
            AuditResult.Success, "User", user.Id.ToString(),
            Details: new { email = user.Email }), cancellationToken);

        return await ToDtoAsync(user);
    }

    public async Task<Result> ResendInviteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return UserNotFound;

        if (!user.IsActive)
            return Error.Conflict("user.inactive", "O usuário está desativado. Ative-o antes de reenviar o convite.");

        if (await userManager.HasPasswordAsync(user))
            return Error.Conflict("user.already_active", "Este usuário já criou a senha. Ele pode usar \"Esqueci minha senha\".");

        var sent = await linkSender.SendInviteAsync(user, cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.UserInviteResent, sent ? AuditResult.Success : AuditResult.Failure,
            "User", user.Id.ToString(), Details: new { email = user.Email }), cancellationToken);

        return sent
            ? Result.Success()
            : Error.Validation("user.invite_not_sent", "Não foi possível enviar o e-mail de convite. Verifique a configuração de e-mail.");
    }

    /// <summary>
    /// Impede que o Administrador se bloqueie e que a plataforma fique sem nenhum Administrador ativo.
    /// </summary>
    private async Task<Error?> GuardAdminRemovalAsync(AppUser target, string selfActionDescription)
    {
        if (target.Id == currentUser.UserId)
            return Error.Forbidden("user.self_protection", $"Você não pode {selfActionDescription}.");

        if (await GetRoleAsync(target) != Roles.Administrador || !target.IsActive)
            return null;

        var activeAdmins = (await userManager.GetUsersInRoleAsync(Roles.Administrador)).Count(u => u.IsActive);
        return activeAdmins <= 1
            ? Error.Conflict("user.last_admin", "A plataforma precisa ter pelo menos um Administrador ativo.")
            : null;
    }

    private Error? ValidateEmail(string email)
    {
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
            return Error.Validation("user.invalid_email", "E-mail inválido.");

        var allowed = accountOptions.Value.AllowedEmailDomains;
        return allowed.Any(d => address.Host.Equals(d, StringComparison.OrdinalIgnoreCase))
            ? null
            : Error.Validation("user.email_domain", $"Use um e-mail corporativo ({string.Join(", ", allowed.Select(d => "@" + d))}).");
    }

    private static Error? ValidateName(string fullName) =>
        string.IsNullOrWhiteSpace(fullName) || fullName.Length > MaxNameLength
            ? Error.Validation("user.invalid_name", $"Informe o nome (até {MaxNameLength} caracteres).")
            : null;

    private static Error? ValidateRole(string role) =>
        Roles.IsValid(role)
            ? null
            : Error.Validation("user.invalid_role", $"Perfil inválido. Use: {string.Join(", ", Roles.All)}.");

    private async Task<string> GetRoleAsync(AppUser user) =>
        (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? Roles.Consulta;

    private async Task<UserDto> ToDtoAsync(AppUser user) =>
        new(user.Id, user.FullName, user.Email!, await GetRoleAsync(user), user.IsActive,
            user.PasswordHash is not null, user.CreatedAt, user.LastLoginAt);
}
