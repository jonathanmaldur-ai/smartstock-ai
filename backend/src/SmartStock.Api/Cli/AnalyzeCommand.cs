using SmartStock.Application.Analysis;

namespace SmartStock.Api.Cli;

/// <summary>
/// Gera a análise do estoque pela linha de comando (usado no fim da importação automática no servidor).
///
///   dotnet run --project backend/src/SmartStock.Api -- analisar
/// </summary>
internal static class AnalyzeCommand
{
    public const string Name = "analisar";

    public static bool IsRequested(string[] args) => args.Length > 0 && args[0].Equals(Name, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IStockAnalysisService>().RunAsync();
        if (!result.IsSuccess)
        {
            Console.WriteLine($"Erro: {result.Error!.Message}");
            return 1;
        }

        var summary = result.Value;
        Console.WriteLine($"Análise gerada com o estoque de {summary.StockDate:dd/MM/yyyy}.");
        foreach (var situation in summary.Situations.OrderBy(s => s.Situation))
            Console.WriteLine($"  {situation.Situation,-18} {situation.Count,8:N0}");
        Console.WriteLine($"Sugestões de transferência pendentes: {summary.Suggestions.Pending:N0} ({summary.Suggestions.PendingUnits:N0} un.)");
        Console.WriteLine($"Sugestões de compra: {summary.PurchaseCount:N0} ({summary.PurchaseUnits:N0} un.)");

        var alerts = await scope.ServiceProvider.GetRequiredService<IAlertService>().GetSummaryAsync();
        Console.WriteLine(alerts.PreviousStockDate is null
            ? "Alertas (sem análise anterior para comparar):"
            : $"Alertas (comparado com o estoque de {alerts.PreviousStockDate:dd/MM/yyyy}):");
        foreach (var type in alerts.Types)
            Console.WriteLine($"  {type.Label,-34} {type.Total,8:N0}  ({type.Unseen:N0} não vistos)");
        return 0;
    }
}
