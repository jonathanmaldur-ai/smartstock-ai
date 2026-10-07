using Microsoft.AspNetCore.Identity;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Auth;
using SmartStock.Application.Common;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Users;
using SmartStock.Infrastructure.Identity;

namespace SmartStock.Infrastructure.Auth;

internal sealed class AuthService(
    UserManager<AppUser> userManager,
    SessionTokenService sessionTokens,
    PasswordLinkSender linkSender,
    IAuditLogger audit,
    TimeProvider clock) : IAuthService
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "E-mail ou senha inválidos.");

    private static readonly Error AccountLocked =
        Error.Locked("auth.locked", "Acesso bloqueado temporariamente por excesso de tentativas. Tente novamente em 15 minutos.");

    private static readonly Error SessionExpired =
        Error.Unauthorized("auth.session_expired", "Sua sessão expirou. Entre novamente.");

    private static readonly Error InvalidLink =
        Error.Validation("auth.invalid_link", "Link inválido ou expirado. Solicite um novo.");

    public async Task<Result<AuthSession>> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim();
        var user = await userManager.FindByEmailAsync(normalizedEmail);

        if (user is null || !user.IsActive || !await userManager.HasPasswordAsync(user))
        {
            await AuditLoginFailure(user, normalizedEmail, user is null ? "unknown_user" : !user.IsActive ? "inactive" : "no_password", cancellationToken);
            return InvalidCredentials;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await AuditLoginFailure(user, normalizedEmail, "locked", cancellationToken);
            return AccountLocked;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            var nowLocked = await userManager.IsLockedOutAsync(user);
            await AuditLoginFailure(user, normalizedEmail, nowLocked ? "wrong_password_locked" : "wrong_password", cancellationToken);
            return nowLocked ? AccountLocked : InvalidCredentials;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = clock.GetUtcNow();
        await userManager.UpdateAsync(user);

        var session = await sessionTokens.IssueAsync(user, await GetRoleAsync(user), cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.LoginSucceeded, AuditResult.Success, "User", user.Id.ToString(),
            UserId: user.Id, UserEmail: user.Email), cancellationToken);

        return session;
    }

    public async Task<Result<AuthSession>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var stored = await sessionTokens.FindAsync(refreshToken, cancellationToken);
        if (stored is null)
            return SessionExpired;

        var now = clock.GetUtcNow();

        if (stored.RevokedAt is not null)
        {
            // Um token já rotacionado foi reapresentado: sinal de roubo. Encerra todas as sessões do usuário.
            if (stored.ReplacedByTokenHash is not null)
                await sessionTokens.RevokeAllAsync(stored.UserId, cancellationToken);
            return SessionExpired;
        }

        if (!stored.IsActive(now) || !stored.User.IsActive)
        {
            await sessionTokens.RevokeAsync(stored, cancellationToken);
            return SessionExpired;
        }

        return await sessionTokens.RotateAsync(stored, await GetRoleAsync(stored.User), cancellationToken);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
            return;

        var stored = await sessionTokens.FindAsync(refreshToken, cancellationToken);
        if (stored is null)
            return;

        await sessionTokens.RevokeAsync(stored, cancellationToken);
        await audit.LogAsync(new AuditEntry(
            AuditActions.Logout, AuditResult.Success, "User", stored.UserId.ToString(),
            UserId: stored.UserId, UserEmail: stored.User.Email), cancellationToken);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim();
        var user = await userManager.FindByEmailAsync(normalizedEmail);

        var sent = false;
        if (user is { IsActive: true })
        {
            // Quem ainda não ativou o acesso recebe um novo convite em vez de um link de recuperação.
            sent = await userManager.HasPasswordAsync(user)
                ? await linkSender.SendPasswordResetAsync(user, cancellationToken)
                : await linkSender.SendInviteAsync(user, cancellationToken);
        }

        await audit.LogAsync(new AuditEntry(
            AuditActions.PasswordResetRequested,
            sent ? AuditResult.Success : AuditResult.Failure,
            "User", user?.Id.ToString(),
            Details: new { email = normalizedEmail, userFound = user is not null, emailSent = sent },
            UserId: user?.Id, UserEmail: normalizedEmail), cancellationToken);
    }

    public async Task<Result> DefinePasswordAsync(DefinePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            await AuditDefineFailure(request, user, "user_not_found_or_inactive", cancellationToken);
            return InvalidLink;
        }

        var result = request.Purpose == PasswordTokenPurpose.Invite
            ? await DefineFirstPasswordAsync(user, request)
            : await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

        if (result is null)
        {
            await AuditDefineFailure(request, user, "invalid_token", cancellationToken);
            return InvalidLink;
        }

        if (!result.Succeeded)
        {
            await AuditDefineFailure(request, user, string.Join(",", result.Errors.Select(e => e.Code)), cancellationToken);
            return ToError(result);
        }

        // Nova senha: encerra sessões abertas e libera um eventual bloqueio por tentativas.
        await sessionTokens.RevokeAllAsync(user.Id, cancellationToken);
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        await audit.LogAsync(new AuditEntry(
            AuditActions.PasswordDefined, AuditResult.Success, "User", user.Id.ToString(),
            Details: new { purpose = request.Purpose.ToString() },
            UserId: user.Id, UserEmail: user.Email), cancellationToken);

        return Result.Success();
    }

    public async Task<Result<SessionUser>> GetSessionUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            return SessionExpired;

        return new SessionUser(user.Id, user.Email!, user.FullName, await GetRoleAsync(user));
    }

    /// <returns>null quando o token do convite é inválido.</returns>
    private async Task<IdentityResult?> DefineFirstPasswordAsync(AppUser user, DefinePasswordRequest request)
    {
        if (await userManager.HasPasswordAsync(user))
            return null;

        var validToken = await userManager.VerifyUserTokenAsync(
            user, InviteTokenProvider.ProviderName, InviteTokenProvider.Purpose, request.Token);
        if (!validToken)
            return null;

        var result = await userManager.AddPasswordAsync(user, request.NewPassword);
        if (result.Succeeded)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        return result;
    }

    private async Task<string> GetRoleAsync(AppUser user) =>
        (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? Roles.Consulta;

    private static Error ToError(IdentityResult result) =>
        result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
            ? InvalidLink
            : Error.Validation("auth.password_policy", string.Join(" ", result.Errors.Select(e => e.Description)));

    private Task AuditLoginFailure(AppUser? user, string email, string reason, CancellationToken cancellationToken) =>
        audit.LogAsync(new AuditEntry(
            AuditActions.LoginFailed, AuditResult.Failure, "User", user?.Id.ToString(),
            Details: new { reason },
            UserId: user?.Id, UserEmail: email), cancellationToken);

    private Task AuditDefineFailure(DefinePasswordRequest request, AppUser? user, string reason, CancellationToken cancellationToken) =>
        audit.LogAsync(new AuditEntry(
            AuditActions.PasswordDefineFailed, AuditResult.Failure, "User", request.UserId.ToString(),
            Details: new { purpose = request.Purpose.ToString(), reason },
            UserId: user?.Id, UserEmail: user?.Email), cancellationToken);
}
