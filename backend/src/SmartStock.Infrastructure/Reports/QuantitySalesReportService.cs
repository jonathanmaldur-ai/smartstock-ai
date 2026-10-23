using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Common;
using SmartStock.Application.Reports;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Reports;

/// <summary>
/// Vendas em quantidade (decisão 62). Lê do banco só peças e quantidades: nenhum valor em R$ é consultado,
/// para não haver como um valor aparecer por engano para Gerente ou Consulta.
/// </summary>
internal sealed class QuantitySalesReportService(SmartStockDbContext db) : IQuantitySalesReportService
{
    public async Task<QuantitySalesReport> BuildAsync(
        DateOnly? from, DateOnly? to, int? storeId, int? productLimit = 100, CancellationToken cancellationToken = default)
    {
        var store = storeId is null ? null : await db.Stores.AsNoTracking().SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);
        var scope = store is null ? "Rede toda" : $"{store.Code} {store.Name}";
        var available = db.DailySales.AsNoTracking().Where(s => storeId == null || s.StoreId == storeId);
        var first = await available.MinAsync(s => (DateOnly?)s.Date, cancellationToken);
        var last = await available.MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (first is null || last is null)
            return new QuantitySalesReport(null, null, null, null, null, null, scope, Totals([]), null, [], [], new QuantityProducts(false, 0, []));

        var (start, end) = SalesPeriod.Resolve(from, to, last.Value);
        var (previousStart, previousEnd) = SalesPeriod.Previous(start, end);
        var rows = await (
                from s in available
                join st in db.Stores on s.StoreId equals st.Id
                where s.Date >= previousStart && s.Date <= end
                select new Row(s.StoreId, st.Code, st.Name, s.Date, s.Pieces))
            .ToListAsync(cancellationToken);
        var current = rows.Where(r => r.Date >= start).ToList();
        var previous = rows.Where(r => r.Date < start).ToList();

        return new QuantitySalesReport(
            start, end, previousStart, previousEnd, first, last, scope,
            Totals(current),
            previous.Count == 0 ? null : Totals(previous),
            current.GroupBy(r => r.Date).OrderBy(g => g.Key).Select(g => new QuantityDay(g.Key, g.Sum(r => r.Pieces))).ToList(),
            Stores(current, previous),
            await ProductsAsync(start, end, storeId, productLimit, cancellationToken));
    }

    public async Task<PagedResult<QuantityProductDay>> ProductDaysAsync(DailySaleProductDayQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize is null ? int.MaxValue : Math.Clamp(query.PageSize.Value, 1, 200);
        var last = await db.DailySales.AsNoTracking()
            .Where(s => query.StoreId == null || s.StoreId == query.StoreId)
            .MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (last is null)
            return new PagedResult<QuantityProductDay>([], page, query.PageSize ?? 0, 0);

        var (start, end) = SalesPeriod.Resolve(query.From, query.To, last.Value);
        var items =
            from i in db.DailySaleItems.AsNoTracking()
            join s in db.Stores on i.StoreId equals s.Id
            join p in db.Products on i.ProductId equals p.Id
            where i.Date >= start && i.Date <= end && (query.StoreId == null || i.StoreId == query.StoreId)
            select new { i.Date, i.StoreId, StoreCode = s.Code, StoreName = s.Name, i.ProductId, p.Code, p.Description, p.Reference, Brand = p.Brand.Name, i.Quantity };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            items = items.Where(x => EF.Functions.ILike(x.Code, pattern) || EF.Functions.ILike(x.Description, pattern) ||
                                     EF.Functions.ILike(x.Brand, pattern) || (x.Reference != null && EF.Functions.ILike(x.Reference, pattern)));
        }

        var total = await items.CountAsync(cancellationToken);
        var ordered = items.OrderByDescending(x => x.Date).ThenBy(x => x.StoreCode).ThenByDescending(x => x.Quantity).ThenBy(x => x.ProductId);
        var rows = await (query.PageSize is null ? ordered : ordered.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<QuantityProductDay>(
            rows.Select(x => new QuantityProductDay(x.Date, x.StoreId, x.StoreCode, x.StoreName, x.ProductId, x.Code, x.Description, x.Brand, x.Quantity)).ToList(),
            page, query.PageSize ?? total, total);
    }

    /// <summary>Produtos vendidos no período, dos que mais venderam em quantidade.</summary>
    private async Task<QuantityProducts> ProductsAsync(DateOnly start, DateOnly end, int? storeId, int? limit, CancellationToken cancellationToken)
    {
        var grouped = db.DailySaleItems.AsNoTracking()
            .Where(i => i.Date >= start && i.Date <= end && (storeId == null || i.StoreId == storeId))
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Quantity = g.Sum(i => i.Quantity),
                Days = g.Select(i => i.Date).Distinct().Count(),
                Stores = g.Select(i => i.StoreId).Distinct().Count()
            });
        var total = await grouped.CountAsync(cancellationToken);
        if (total == 0)
            return new QuantityProducts(false, 0, []);

        var ordered = grouped.OrderByDescending(g => g.Quantity).ThenBy(g => g.ProductId);
        var rows = await (
                from g in limit is null ? ordered : ordered.Take(limit.Value)
                join p in db.Products on g.ProductId equals p.Id
                select new QuantityProduct(g.ProductId, p.Code, p.Description, p.Brand.Name, g.Quantity, g.Days, g.Stores))
            .ToListAsync(cancellationToken);
        return new QuantityProducts(true, total, rows.OrderByDescending(r => r.Quantity).ThenBy(r => r.ProductId).ToList());
    }

    private static QuantityTotals Totals(List<Row> rows)
    {
        var pieces = rows.Sum(r => r.Pieces);
        var days = rows.Select(r => r.Date).Distinct().Count();
        return new QuantityTotals(pieces, days, days == 0 ? null : Math.Round(pieces / days, 1));
    }

    private static List<QuantityStore> Stores(List<Row> current, List<Row> previous) =>
        current.GroupBy(r => (r.StoreId, r.Code, r.Name))
            .Select(g =>
            {
                var best = g.MaxBy(r => r.Pieces)!;
                var before = previous.Where(r => r.StoreId == g.Key.StoreId).ToList();
                return new QuantityStore(
                    g.Key.StoreId, g.Key.Code, g.Key.Name, g.Sum(r => r.Pieces),
                    before.Count == 0 ? null : before.Sum(r => r.Pieces), best.Date, best.Pieces);
            })
            .OrderByDescending(s => s.Pieces)
            .ToList();

    private sealed record Row(int StoreId, string Code, string Name, DateOnly Date, decimal Pieces);
}
