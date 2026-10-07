using SmartStock.Application.Common;

namespace SmartStock.Application.Reports;

/// <summary>Relatório mensal (Fase 4.2, decisões 11 e 42): a análise atual comparada com uma anterior.</summary>
public interface IMonthlyReportService
{
    /// <param name="compareWith">Análise anterior; sem ela, usa a mais recente com estoque de data anterior.</param>
    Task<Result<MonthlyReport>> BuildAsync(Guid? compareWith, CancellationToken cancellationToken = default);
}

/// <param name="Previous">null quando ainda não existe análise anterior para comparar.</param>
/// <param name="Options">Análises anteriores que podem ser escolhidas para comparar.</param>
public sealed record MonthlyReport(
    AnalysisRef Current,
    AnalysisRef? Previous,
    IReadOnlyList<AnalysisRef> Options,
    IReadOnlyList<IndicatorComparison> Indicators,
    IReadOnlyList<StoreComparison> Stores,
    TransferPerformance Transfers);

public sealed record AnalysisRef(Guid Id, DateOnly AnalysisDate, DateOnly StockDate, DateTimeOffset CreatedAt);

/// <param name="LowerIsBetter">Para colorir a variação: menos ruptura é melhor; mais estoque não é, por si, melhor nem pior.</param>
public sealed record IndicatorComparison(string Name, decimal Current, decimal? Previous, bool? LowerIsBetter, string Unit);

public sealed record StoreComparison(
    string Code,
    string Name,
    StoreMetrics Current,
    StoreMetrics? Previous);

public sealed record StoreMetrics(int RelevantRuptures, int NegativeItems, int Excess, int Stagnant, decimal StockUnits, decimal? CoverageDays);

/// <summary>
/// Sugestões decididas desde a análise anterior (ou todas, se não houver).
/// Precisão = realizadas ÷ aprovadas (inclui as já realizadas).
/// </summary>
public sealed record TransferPerformance(
    DateTimeOffset? Since,
    int Approved,
    decimal ApprovedUnits,
    int Completed,
    decimal CompletedUnits,
    int Rejected,
    int PendingApproval,
    decimal? PrecisionPercent,
    decimal? AverageDaysToComplete);
