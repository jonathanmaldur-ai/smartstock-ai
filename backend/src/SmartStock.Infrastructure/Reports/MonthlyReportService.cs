using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Common;
using SmartStock.Application.Reports;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Reports;

/// <summary>Relatório mensal (decisão 42): indicadores e lojas, antes × agora, e desempenho das transferências.</summary>
internal sealed class MonthlyReportService(SmartStockDbContext db) : IMonthlyReportService
{
    public async Task<Result<MonthlyReport>> BuildAsync(Guid? compareWith, CancellationToken cancellationToken = default)
    {
        var analyses = await db.StockAnalyses.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AnalysisRef(a.Id, a.AnalysisDate, a.StockDate, a.CreatedAt))
            .ToListAsync(cancellationToken);
        if (analyses.Count == 0)
            return Error.Conflict("report.no_analysis", "Gere a análise do estoque antes de emitir relatórios.");

        var current = analyses[0];
        var options = analyses.Skip(1).ToList();
        var previous = compareWith is null
            ? options.FirstOrDefault(a => a.StockDate < current.StockDate) ?? options.FirstOrDefault()
            : options.FirstOrDefault(a => a.Id == compareWith);
        if (compareWith is not null && previous is null)
            return Error.NotFound("report.analysis_not_found", "Análise para comparar não encontrada.");

        var now = await MetricsAsync(current.Id, cancellationToken);
        var before = previous is null ? null : await MetricsAsync(previous.Id, cancellationToken);

        return new MonthlyReport(
            current, previous, options,
            Indicators(now, before),
            await StoresAsync(now, before, cancellationToken),
            await TransfersAsync(previous?.CreatedAt, cancellationToken));
    }

    private static List<IndicatorComparison> Indicators(AnalysisMetrics now, AnalysisMetrics? before)
    {
        decimal? Prev(Func<AnalysisMetrics, decimal> pick) => before is null ? null : pick(before);
        return
        [
            new("Rupturas que importam", now.RelevantRuptures, Prev(m => m.RelevantRuptures), true, "itens"),
            new("Itens com estoque negativo", now.NegativeItems, Prev(m => m.NegativeItems), true, "itens"),
            new("Unidades negativas", now.NegativeUnits, Prev(m => m.NegativeUnits), false, "un."),
            new("Itens em excesso", now.Excess, Prev(m => m.Excess), true, "itens"),
            new("Itens parados", now.Stagnant, Prev(m => m.Stagnant), true, "itens"),
            new("Estoque total da rede", now.StockUnits, Prev(m => m.StockUnits), null, "un."),
            new("Venda de 12 meses", now.Sold, Prev(m => m.Sold), false, "un."),
            new("Cobertura da rede", now.CoverageDays ?? 0, before is null ? null : before.CoverageDays ?? 0, null, "dias")
        ];
    }

    private async Task<List<StoreComparison>> StoresAsync(AnalysisMetrics now, AnalysisMetrics? before, CancellationToken cancellationToken)
    {
        var stores = await db.Stores.AsNoTracking().Where(s => s.Status == StoreStatus.Active).OrderBy(s => s.Code).ToListAsync(cancellationToken);
        return stores
            .Where(s => now.ByStore.ContainsKey(s.Id))
            .Select(s => new StoreComparison(s.Code, s.Name, now.ByStore[s.Id], before?.ByStore.GetValueOrDefault(s.Id)))
            .ToList();
    }

    /// <summary>Sugestões decididas desde a análise anterior. "Realizada" conta também como aprovada.</summary>
    private async Task<TransferPerformance> TransfersAsync(DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var decided = await db.TransferSuggestions.AsNoTracking()
            .Where(s => s.DecidedAt != null && (since == null || s.DecidedAt >= since))
            .Select(s => new { s.Status, s.Quantity, s.DecidedAt, s.CompletedOn, s.CompletedQuantity })
            .ToListAsync(cancellationToken);
        var pending = await db.TransferSuggestions.CountAsync(s => s.Status == SuggestionStatus.Suggested, cancellationToken);

        var approved = decided.Where(s => s.Status is SuggestionStatus.Approved or SuggestionStatus.Completed).ToList();
        var completed = decided.Where(s => s.Status == SuggestionStatus.Completed).ToList();
        var days = completed
            .Where(s => s.CompletedOn is not null)
            .Select(s => s.CompletedOn!.Value.DayNumber - DateOnly.FromDateTime(s.DecidedAt!.Value.ToLocalTime().DateTime).DayNumber)
            .ToList();

        return new TransferPerformance(
            since,
            approved.Count, approved.Sum(s => s.Quantity),
            completed.Count, completed.Sum(s => s.CompletedQuantity ?? s.Quantity),
            decided.Count(s => s.Status == SuggestionStatus.Rejected),
            pending,
            approved.Count == 0 ? null : Math.Round(100m * completed.Count / approved.Count, 1),
            days.Count == 0 ? null : Math.Round((decimal)days.Average(), 1));
    }

    private async Task<AnalysisMetrics> MetricsAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        var byStore = await db.StockPositions.AsNoTracking()
            .Where(p => p.AnalysisId == analysisId)
            .GroupBy(p => p.StoreId)
            .Select(g => new
            {
                StoreId = g.Key,
                Relevant = g.Count(p => p.Situation == StockSituation.Rupture && p.Priority == AlertPriority.Critical),
                Excess = g.Count(p => p.Situation == StockSituation.Excess),
                Stagnant = g.Count(p => p.Situation == StockSituation.Stagnant),
                Stock = g.Where(p => p.Stock > 0).Sum(p => p.Stock),
                Sold = g.Sum(p => p.Sold12Months),
                IsWarehouse = g.Any(p => p.Situation == StockSituation.Warehouse)
            })
            .ToListAsync(cancellationToken);
        var negatives = await db.NegativeStocks.AsNoTracking()
            .Where(n => n.AnalysisId == analysisId)
            .GroupBy(n => n.StoreId)
            .Select(g => new { StoreId = g.Key, Items = g.Count(), Units = g.Sum(n => n.Quantity) })
            .ToDictionaryAsync(g => g.StoreId, cancellationToken);

        var stores = byStore.ToDictionary(
            s => s.StoreId,
            s =>
            {
                var coverage = s.IsWarehouse ? null : StockMetrics.CoverageDays(s.Stock, StockMetrics.DailyAverage(s.Sold));
                return new StoreMetrics(s.Relevant, negatives.GetValueOrDefault(s.StoreId)?.Items ?? 0, s.Excess, s.Stagnant, s.Stock,
                    coverage is null ? null : Math.Round(coverage.Value, 0));
            });

        var stock = byStore.Sum(s => s.Stock);
        var sold = byStore.Sum(s => s.Sold);
        var networkCoverage = StockMetrics.CoverageDays(stock, StockMetrics.DailyAverage(sold));
        return new AnalysisMetrics(
            byStore.Sum(s => s.Relevant), negatives.Values.Sum(n => n.Items), negatives.Values.Sum(n => n.Units),
            byStore.Sum(s => s.Excess), byStore.Sum(s => s.Stagnant), stock, sold,
            networkCoverage is null ? null : Math.Round(networkCoverage.Value, 0), stores);
    }

    private sealed record AnalysisMetrics(
        int RelevantRuptures, int NegativeItems, decimal NegativeUnits, int Excess, int Stagnant, decimal StockUnits, decimal Sold,
        decimal? CoverageDays, Dictionary<int, StoreMetrics> ByStore);
}
