using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartStock.Application.Abstractions;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Users;
using SmartStock.Infrastructure.Email;
using SmartStock.Infrastructure.Identity;

namespace SmartStock.Infrastructure.Analysis;

/// <summary>
/// Resumo dos alertas por e-mail aos Administradores e Gerentes ativos, após cada análise (decisão 47), quando ligado
/// nos parâmetros. Falha no envio não derruba a análise: só fica no log.
/// </summary>
internal sealed class AlertNotifier(
    UserManager<AppUser> userManager,
    IEmailSender emailSender,
    IOptions<AppOptions> appOptions,
    ILogger<AlertNotifier> logger)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task NotifyAsync(StockAnalysis analysis, IReadOnlyCollection<StockAlert> alerts, CancellationToken cancellationToken)
    {
        var fresh = alerts.Where(a => a.SeenAt is null).ToList();
        if (fresh.Count == 0)
            return;

        var recipients = (await userManager.GetUsersInRoleAsync(Roles.Administrador))
            .Concat(await userManager.GetUsersInRoleAsync(Roles.Gerente))
            .Where(u => u.IsActive && u.EmailConfirmed && u.Email is not null)
            .DistinctBy(u => u.Id)
            .ToList();

        var summary = Summary(analysis, fresh);
        var link = $"{appOptions.Value.PublicUrl.TrimEnd('/')}/alertas";
        foreach (var user in recipients)
        {
            try
            {
                await emailSender.SendAsync(EmailTemplates.AlertSummary(user.Email!, user.FullName, summary, link), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao enviar o resumo de alertas para {To}.", user.Email);
            }
        }
    }

    private static string Summary(StockAnalysis analysis, List<StockAlert> alerts)
    {
        var byType = alerts
            .GroupBy(a => a.Type)
            .OrderByDescending(g => g.Max(a => a.Priority)).ThenByDescending(g => g.Count())
            .Select(g => $"{AlertTypeLabels.Of(g.Key)}: {g.Count().ToString("#,##0", PtBr)}");
        return $"A análise do estoque de {analysis.StockDate.ToString("dd/MM/yyyy", PtBr)} tem {alerts.Count.ToString("#,##0", PtBr)} " +
               $"alertas ainda não vistos. {string.Join(" · ", byType)}.";
    }
}
