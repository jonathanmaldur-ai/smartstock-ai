using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartStock.Application.Abstractions;
using SmartStock.Infrastructure.Email;
using SmartStock.Infrastructure.Identity;

namespace SmartStock.Infrastructure.Auth;

/// <summary>
/// Gera os links de primeiro acesso e de recuperação de senha e envia por e-mail.
/// </summary>
internal sealed class PasswordLinkSender(
    UserManager<AppUser> userManager,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    IOptions<AccountOptions> accountOptions,
    ILogger<PasswordLinkSender> logger)
{
    public const string InviteLinkType = "convite";
    public const string ResetLinkType = "recuperacao";

    /// <returns>true se o e-mail foi enviado.</returns>
    public async Task<bool> SendInviteAsync(AppUser user, CancellationToken cancellationToken)
    {
        var token = await userManager.GenerateUserTokenAsync(
            user, InviteTokenProvider.ProviderName, InviteTokenProvider.Purpose);

        var message = EmailTemplates.Invite(
            user.Email!, user.FullName, BuildLink(user, token, InviteLinkType), accountOptions.Value.InviteLinkHours);

        return await TrySendAsync(message, cancellationToken);
    }

    /// <returns>true se o e-mail foi enviado.</returns>
    public async Task<bool> SendPasswordResetAsync(AppUser user, CancellationToken cancellationToken)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var message = EmailTemplates.PasswordReset(
            user.Email!, user.FullName, BuildLink(user, token, ResetLinkType), accountOptions.Value.ResetLinkHours);

        return await TrySendAsync(message, cancellationToken);
    }

    private string BuildLink(AppUser user, string token, string type)
    {
        var baseUrl = appOptions.Value.PublicUrl.TrimEnd('/');
        return $"{baseUrl}/definir-senha?tipo={type}&uid={user.Id}&token={Uri.EscapeDataString(token)}";
    }

    private async Task<bool> TrySendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao enviar o e-mail \"{Subject}\" para {To}.", message.Subject, message.To);
            return false;
        }
    }
}
