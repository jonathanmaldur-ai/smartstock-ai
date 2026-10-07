using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartStock.Application.Abstractions;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Users;
using SmartStock.Infrastructure.Auth;
using SmartStock.Infrastructure.Identity;

namespace SmartStock.Infrastructure.Seeding;

/// <summary>
/// Garante os perfis do PRD e, se o banco não tiver nenhum usuário, cria o Administrador inicial
/// (sem senha) e envia o convite de primeiro acesso. Idempotente.
/// </summary>
internal sealed class IdentitySeeder(
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<AppUser> userManager,
    PasswordLinkSender linkSender,
    IAuditLogger audit,
    IOptions<AccountOptions> accountOptions,
    TimeProvider clock,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
        }

        if (userManager.Users.Any())
            return;

        var options = accountOptions.Value;
        var admin = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = options.InitialAdminEmail.ToLowerInvariant(),
            Email = options.InitialAdminEmail.ToLowerInvariant(),
            FullName = options.InitialAdminName,
            IsActive = true,
            CreatedAt = clock.GetUtcNow()
        };

        var created = await userManager.CreateAsync(admin);
        if (!created.Succeeded)
            throw new InvalidOperationException(
                "Não foi possível criar o Administrador inicial: " + string.Join(" ", created.Errors.Select(e => e.Description)));

        await userManager.AddToRoleAsync(admin, Roles.Administrador);

        var inviteSent = await linkSender.SendInviteAsync(admin, cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.AdminSeeded, AuditResult.Success, "User", admin.Id.ToString(),
            Details: new { email = admin.Email, inviteSent }, UserEmail: "sistema"), cancellationToken);

        logger.LogWarning("Administrador inicial criado: {Email}. Convite enviado: {InviteSent}.", admin.Email, inviteSent);
    }
}
