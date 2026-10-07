using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

/// <summary>
/// Alertas de uma análise (decisão 47): compara com a última análise de estoque anterior e com as transferências
/// entre as duas fotos. O "já vi" de um alerta que continua aparecendo é mantido.
/// </summary>
internal sealed class AlertCalculator(SmartStockDbContext db)
{
    public sealed record Entry(
        int ProductId, int StoreId, bool IsWarehouse, decimal Stock, decimal Sold12Months,
        decimal DailyAverage, decimal? CoverageDays, StockSituation Situation);

    public async Task<List<StockAlert>> CalculateAsync(
        Guid analysisId, DateOnly stockDate, StockParameters parameters, IReadOnlyList<Entry> entries, CancellationToken cancellationToken)
    {
        var previous = await db.StockAnalyses.AsNoTracking()
            .Where(a => a.StockDate < stockDate)
            .OrderByDescending(a => a.StockDate).ThenByDescending(a => a.CreatedAt)
            .Select(a => new { a.StockImportId, a.StockDate })
            .FirstOrDefaultAsync(cancellationToken);

        var previousStock = previous is null ? null : await StockAsync(previous.StockImportId, cancellationToken);
        var movements = previous is null ? [] : await MovementsAsync(previous.StockDate, stockDate, cancellationToken);
        var brands = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.BrandId, cancellationToken);
        var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => $"{s.Code} {s.Name}", cancellationToken);

        var positions = entries.Select(e => new AlertPosition(
                e.ProductId, e.StoreId, brands[e.ProductId], e.IsWarehouse,
                e.Stock, e.Sold12Months, e.DailyAverage, e.CoverageDays, e.Situation,
                previousStock is null ? null : previousStock.GetValueOrDefault((e.ProductId, e.StoreId)),
                movements.GetValueOrDefault((e.ProductId, e.StoreId, TransferDirection.In)),
                movements.GetValueOrDefault((e.ProductId, e.StoreId, TransferDirection.Out))))
            .ToList();

        var rules = new AlertRules(parameters, id => stores.GetValueOrDefault(id, id.ToString()), previous?.StockDate);
        var seen = await SeenAsync(cancellationToken);

        return rules.Evaluate(positions)
            .Select(c =>
            {
                var alert = new StockAlert
                {
                    AnalysisId = analysisId, Type = c.Type, Priority = c.Priority,
                    ProductId = c.ProductId, StoreId = c.StoreId, BrandId = c.BrandId,
                    Message = c.Message.Length > 1000 ? c.Message[..1000] : c.Message
                };
                if (seen.TryGetValue(alert.Key, out var mark))
                    (alert.SeenAt, alert.SeenByEmail) = mark;
                return alert;
            })
            .ToList();
    }

    private async Task<Dictionary<(int, int), decimal>> StockAsync(Guid importId, CancellationToken cancellationToken) =>
        await db.StockLevels.AsNoTracking()
            .Where(s => s.ImportId == importId)
            .ToDictionaryAsync(s => (s.ProductId, s.StoreId), s => s.Quantity, cancellationToken);

    /// <summary>Entradas no destino e saídas da origem depois da foto anterior, até a atual (cancelamentos não contam).</summary>
    private async Task<Dictionary<(int, int, TransferDirection), decimal>> MovementsAsync(DateOnly after, DateOnly until, CancellationToken cancellationToken)
    {
        var rows = await db.TransferMovements.AsNoTracking()
            .Where(m => m.Date > after && m.Date <= until && !m.IsCancellation)
            .GroupBy(m => new { m.ProductId, m.OriginStoreId, m.DestinationStoreId, m.Direction })
            .Select(g => new { g.Key.ProductId, g.Key.OriginStoreId, g.Key.DestinationStoreId, g.Key.Direction, Quantity = g.Sum(m => m.Quantity) })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<(int, int, TransferDirection), decimal>();
        foreach (var r in rows)
        {
            var store = r.Direction == TransferDirection.In ? r.DestinationStoreId : r.OriginStoreId;
            var key = (r.ProductId, store, r.Direction);
            result[key] = result.GetValueOrDefault(key) + r.Quantity;
        }
        return result;
    }

    /// <summary>Alertas marcados como vistos na análise mais recente.</summary>
    private async Task<Dictionary<string, (DateTimeOffset?, string?)>> SeenAsync(CancellationToken cancellationToken)
    {
        var latest = await db.StockAnalyses.AsNoTracking().OrderByDescending(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
            return [];
        var seen = await db.StockAlerts.AsNoTracking()
            .Where(a => a.AnalysisId == latest && a.SeenAt != null)
            .ToListAsync(cancellationToken);
        return seen.ToDictionary(a => a.Key, a => (a.SeenAt, a.SeenByEmail));
    }
}
