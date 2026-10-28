using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Analysis;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

/// <summary>
/// Venda recente por produto e loja, das vendas por dia (decisão 52), para a sazonalidade (decisão 65).
/// Só entram as lojas com venda importada em pelo menos 80% dos dias da janela: dias faltando não viram "parou de vender".
/// </summary>
internal sealed class RecentSalesLoader(SmartStockDbContext db)
{
    /// <summary>Vendas por dia mais antigas que isto (em relação à análise) não contam como recentes.</summary>
    private const int MaxStaleDays = 15;

    public sealed record Window(DateOnly From, DateOnly To, IReadOnlySet<int> CoveredStores, IReadOnlyDictionary<(int ProductId, int StoreId), decimal> Sold)
    {
        public decimal SoldIn(int productId, int storeId) => Sold.GetValueOrDefault((productId, storeId));
    }

    /// <returns>null quando não há vendas por dia recentes.</returns>
    public async Task<Window?> LoadAsync(int days, DateOnly analysisDate, CancellationToken cancellationToken)
    {
        var last = await db.DailySaleItems.MaxAsync(d => (DateOnly?)d.Date, cancellationToken);
        if (last is null || last.Value < analysisDate.AddDays(-MaxStaleDays))
            return null;

        var from = last.Value.AddDays(-(days - 1));
        var inWindow = db.DailySaleItems.AsNoTracking().Where(d => d.Date >= from && d.Date <= last.Value);
        var daysByStore = await inWindow
            .Select(d => new { d.StoreId, d.Date }).Distinct()
            .GroupBy(d => d.StoreId)
            .Select(g => new { StoreId = g.Key, Days = g.Count() })
            .ToListAsync(cancellationToken);
        var covered = daysByStore.Where(s => s.Days >= days * SeasonalRules.MinimumDaysCoverage).Select(s => s.StoreId).ToHashSet();

        var sold = await inWindow
            .Where(d => covered.Contains(d.StoreId))
            .GroupBy(d => new { d.ProductId, d.StoreId })
            .Select(g => new { g.Key.ProductId, g.Key.StoreId, Quantity = g.Sum(d => d.Quantity) })
            .ToDictionaryAsync(x => (x.ProductId, x.StoreId), x => x.Quantity, cancellationToken);
        return new Window(from, last.Value, covered, sold);
    }
}
