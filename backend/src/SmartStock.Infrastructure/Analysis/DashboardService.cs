using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Analysis;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

internal sealed class DashboardService(SmartStockDbContext db, TimeProvider clock) : IDashboardService
{
    /// <summary>Decisão 19: prazos para o aviso de dados desatualizados.</summary>
    private const int StockMaxAgeDays = 7;
    private const int SalesAndTransfersMaxAgeDays = 30;

    private const int TopProductsSize = 10;
    private const int TrendSize = 12;

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var freshness = await FreshnessAsync(today, cancellationToken);
        var latest = await db.StockAnalyses.AsNoTracking().OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
            return new DashboardDto(freshness, null, [], [], []);

        var stores = await StoresAsync(latest.Id, cancellationToken);
        return new DashboardDto(
            freshness,
            await KpisAsync(latest, stores, cancellationToken),
            stores,
            await TopProductsAsync(latest.Id, cancellationToken),
            await TrendAsync(cancellationToken));
    }

    private async Task<DataFreshness> FreshnessAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var stockDate = (await CurrentImports.StockBatchAsync(db, cancellationToken))?.ReferenceDate;
        var salesBatches = await CurrentImports.SalesBatchesAsync(db, cancellationToken);
        // Vendas: a loja com os dados mais antigos define a idade.
        var salesDate = salesBatches.Count == 0 ? (DateOnly?)null : salesBatches.Values.Min(b => b.ReferenceDate);
        var transfersDate = await db.TransferMovements.AsNoTracking().MaxAsync(m => (DateOnly?)m.Date, cancellationToken);

        int? Age(DateOnly? date) => date is null ? null : today.DayNumber - date.Value.DayNumber;
        var (stockAge, salesAge, transfersAge) = (Age(stockDate), Age(salesDate), Age(transfersDate));
        return new DataFreshness(
            stockDate, stockAge, stockAge > StockMaxAgeDays,
            salesDate, salesAge, salesAge > SalesAndTransfersMaxAgeDays,
            transfersDate, transfersAge, transfersAge > SalesAndTransfersMaxAgeDays);
    }

    private async Task<List<StoreDashboardRow>> StoresAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        var rows = await db.StockPositions.AsNoTracking()
            .Where(p => p.AnalysisId == analysisId)
            .GroupBy(p => p.StoreId)
            .Select(g => new
            {
                StoreId = g.Key,
                Stock = g.Where(p => p.Stock > 0).Sum(p => p.Stock),
                Sold = g.Sum(p => p.Sold12Months),
                Rupture = g.Count(p => p.Situation == StockSituation.Rupture),
                Below = g.Count(p => p.Situation == StockSituation.BelowMinimum || p.Situation == StockSituation.CriticalCoverage),
                Normal = g.Count(p => p.Situation == StockSituation.Normal),
                Excess = g.Count(p => p.Situation == StockSituation.Excess),
                Stagnant = g.Count(p => p.Situation == StockSituation.Stagnant)
            })
            .ToListAsync(cancellationToken);

        var stores = await db.Stores.AsNoTracking().Where(s => s.Status == StoreStatus.Active).ToDictionaryAsync(s => s.Id, cancellationToken);
        return rows
            .Where(r => stores.ContainsKey(r.StoreId))
            .Select(r =>
            {
                var store = stores[r.StoreId];
                var coverage = store.Type == StoreType.Warehouse ? null : StockMetrics.CoverageDays(r.Stock, StockMetrics.DailyAverage(r.Sold));
                return new StoreDashboardRow(store.Id, store.Code, store.Name, store.Type, r.Stock, r.Sold,
                    coverage is null ? null : Math.Round(coverage.Value, 1), r.Rupture, r.Below, r.Normal, r.Excess, r.Stagnant);
            })
            .OrderBy(r => r.Code)
            .ToList();
    }

    private async Task<DashboardKpis> KpisAsync(StockAnalysis analysis, List<StoreDashboardRow> stores, CancellationToken cancellationToken)
    {
        var stock = stores.Sum(s => s.StockUnits);
        var sold = stores.Sum(s => s.Sold12Months);
        var coverage = StockMetrics.CoverageDays(stock, StockMetrics.DailyAverage(sold));

        var ruptures = await RelevantRupturesQuery().CountAsync(p => p.AnalysisId == analysis.Id, cancellationToken);
        var pending = await db.TransferSuggestions.AsNoTracking()
            .Where(s => s.AnalysisId == analysis.Id && s.Status == SuggestionStatus.Suggested)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Units = g.Sum(s => s.Quantity) })
            .FirstOrDefaultAsync(cancellationToken);
        var negatives = await db.NegativeStocks.AsNoTracking().CountAsync(n => n.AnalysisId == analysis.Id, cancellationToken);

        return new DashboardKpis(
            analysis.AnalysisDate, analysis.StockDate, stock, sold,
            stock > 0 ? Math.Round(sold / stock, 2) : null,
            coverage is null ? null : Math.Round(coverage.Value, 0),
            ruptures, pending?.Count ?? 0, pending?.Units ?? 0, negatives);
    }

    private async Task<List<TopProductDto>> TopProductsAsync(Guid analysisId, CancellationToken cancellationToken)
    {
        var top = await db.StockPositions.AsNoTracking()
            .Where(p => p.AnalysisId == analysisId)
            .GroupBy(p => p.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Sold = g.Sum(p => p.Sold12Months),
                Stock = g.Where(p => p.Stock > 0).Sum(p => p.Stock),
                Ruptures = g.Count(p => p.Situation == StockSituation.Rupture && p.Priority == AlertPriority.Critical)
            })
            .OrderByDescending(g => g.Sold)
            .Take(TopProductsSize)
            .ToListAsync(cancellationToken);

        var ids = top.Select(t => t.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.Description, BrandName = p.Brand.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return top
            .Select(t => new TopProductDto(t.ProductId, products[t.ProductId].Code, products[t.ProductId].Description,
                products[t.ProductId].BrandName, t.Sold, t.Stock, t.Ruptures))
            .ToList();
    }

    /// <summary>Uma linha por análise (as mais recentes), em ordem cronológica.</summary>
    private async Task<List<TrendPoint>> TrendAsync(CancellationToken cancellationToken)
    {
        var analyses = await db.StockAnalyses.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).Take(TrendSize)
            .Select(a => new { a.Id, a.AnalysisDate, a.StockDate, a.SuggestionCount, a.CreatedAt })
            .ToListAsync(cancellationToken);
        var ids = analyses.Select(a => a.Id).ToList();

        var ruptures = await RelevantRupturesQuery()
            .Where(p => ids.Contains(p.AnalysisId))
            .GroupBy(p => p.AnalysisId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var negatives = await db.NegativeStocks.AsNoTracking()
            .Where(n => ids.Contains(n.AnalysisId))
            .GroupBy(n => n.AnalysisId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return analyses
            .OrderBy(a => a.CreatedAt)
            .Select(a => new TrendPoint(a.AnalysisDate, a.StockDate, ruptures.GetValueOrDefault(a.Id), negatives.GetValueOrDefault(a.Id), a.SuggestionCount))
            .ToList();
    }

    private IQueryable<StockPosition> RelevantRupturesQuery() =>
        db.StockPositions.AsNoTracking().Where(p => p.Situation == StockSituation.Rupture && p.Priority == AlertPriority.Critical);
}
