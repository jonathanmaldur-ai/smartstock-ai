using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Common;
using SmartStock.Application.Reports;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Reports;

/// <summary>Vendas por dia e por loja (decisão 51): totais, comparação com o período anterior, lojas e dia da semana.</summary>
internal sealed class DailySalesReportService(SmartStockDbContext db) : IDailySalesReportService
{
    private const int DefaultDays = 30;
    private const int MaxDays = 400;

    /// <summary>Limite da lista de vendas de um produto na tela (o Excel traz todas).</summary>
    private const int MaxSaleLines = 500;
    private static readonly string[] WeekdayLabels = ["Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado"];

    public async Task<DailySalesReport> BuildAsync(
        DateOnly? from, DateOnly? to, int? storeId, int? productLimit = 100, CancellationToken cancellationToken = default)
    {
        var store = storeId is null ? null : await db.Stores.AsNoTracking().SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);
        var scope = store is null ? "Rede toda" : $"{store.Code} {store.Name}";
        var available = db.DailySales.AsNoTracking().Where(s => storeId == null || s.StoreId == storeId);
        var first = await available.MinAsync(s => (DateOnly?)s.Date, cancellationToken);
        var last = await available.MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (first is null || last is null)
            return new DailySalesReport(null, null, null, null, null, null, scope, Totals([]), null, [], [], [], new DailySalesProducts(false, 0, []));

        var (start, end) = Period(from, to, last.Value);
        var length = end.DayNumber - start.DayNumber + 1;
        var previousEnd = start.AddDays(-1);
        var previousStart = previousEnd.AddDays(-(length - 1));

        var rows = await (
                from s in available
                join st in db.Stores on s.StoreId equals st.Id
                where s.Date >= previousStart && s.Date <= end
                select new Row(s.StoreId, st.Code, st.Name, s.Date, s.Sales, s.Pieces, s.Gross, s.Net))
            .ToListAsync(cancellationToken);
        var current = rows.Where(r => r.Date >= start).ToList();
        var previous = rows.Where(r => r.Date < start).ToList();

        return new DailySalesReport(
            start, end, previousStart, previousEnd, first, last, scope,
            Totals(current),
            previous.Count == 0 ? null : Totals(previous),
            ByDay(current).Select(d => new DailySalesDay(d.Date, d.Net, d.Sales, d.Pieces)).ToList(),
            Stores(current, previous),
            Weekdays(current),
            await ProductsAsync(start, end, storeId, productLimit, cancellationToken));
    }

    public async Task<PagedResult<DailySaleProductDay>> ProductDaysAsync(DailySaleProductDayQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize is null ? int.MaxValue : Math.Clamp(query.PageSize.Value, 1, 200);
        var last = await db.DailySales.AsNoTracking()
            .Where(s => query.StoreId == null || s.StoreId == query.StoreId)
            .MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (last is null)
            return new PagedResult<DailySaleProductDay>([], page, query.PageSize ?? 0, 0);

        var (start, end) = Period(query.From, query.To, last.Value);
        var items =
            from i in db.DailySaleItems.AsNoTracking()
            join s in db.Stores on i.StoreId equals s.Id
            join p in db.Products on i.ProductId equals p.Id
            where i.Date >= start && i.Date <= end && (query.StoreId == null || i.StoreId == query.StoreId)
            select new { i.Date, i.StoreId, StoreCode = s.Code, StoreName = s.Name, i.ProductId, p.Code, p.Description, p.Reference, Brand = p.Brand.Name, i.Quantity, i.Amount, i.Sales };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            items = items.Where(x => EF.Functions.ILike(x.Code, pattern) || EF.Functions.ILike(x.Description, pattern) ||
                                     EF.Functions.ILike(x.Brand, pattern) || (x.Reference != null && EF.Functions.ILike(x.Reference, pattern)));
        }

        var total = await items.CountAsync(cancellationToken);
        var ordered = items.OrderByDescending(x => x.Date).ThenBy(x => x.StoreCode).ThenByDescending(x => x.Amount).ThenBy(x => x.ProductId);
        var rows = await (query.PageSize is null ? ordered : ordered.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<DailySaleProductDay>(
            rows.Select(x => new DailySaleProductDay(x.Date, x.StoreCode, x.StoreName, x.ProductId, x.Code, x.Description, x.Brand, x.Quantity, x.Amount, x.Sales, x.StoreId)).ToList(),
            page, query.PageSize ?? total, total);
    }

    public async Task<IReadOnlyList<SaleLineDetail>> SaleLinesAsync(
        int? storeId, DateOnly from, DateOnly to, int productId, CancellationToken cancellationToken = default)
    {
        var lines = await (
                from l in db.StoreSaleLines.AsNoTracking()
                join s in db.StoreSales on new { l.StoreId, Number = l.SaleNumber } equals new { s.StoreId, s.Number }
                join st in db.Stores on l.StoreId equals st.Id
                where (storeId == null || l.StoreId == storeId) && l.Date >= @from && l.Date <= to && l.ProductId == productId
                select new
                {
                    l.Date, StoreCode = st.Code, StoreName = st.Name, l.SaleNumber, l.Quantity, l.UnitPrice, l.Amount, SaleTotal = s.Net,
                    SaleItems = db.StoreSaleLines.Count(x => x.StoreId == l.StoreId && x.SaleNumber == l.SaleNumber),
                    s.RegisteredCustomer, s.Payment, s.FiscalDocument
                })
            .OrderByDescending(x => x.Date).ThenBy(x => x.StoreCode).ThenByDescending(x => x.Quantity).ThenBy(x => x.SaleNumber)
            .Take(MaxSaleLines)
            .ToListAsync(cancellationToken);
        return lines.Select(x => new SaleLineDetail(
                x.Date, x.StoreCode, x.StoreName, x.SaleNumber, x.Quantity, x.UnitPrice, x.Amount, x.SaleTotal, x.SaleItems, x.RegisteredCustomer, x.Payment, x.FiscalDocument))
            .ToList();
    }

    public async Task<IReadOnlyList<SaleLineExport>> AllSaleLinesAsync(DateOnly? from, DateOnly? to, int? storeId, CancellationToken cancellationToken = default)
    {
        var last = await db.StoreSales.AsNoTracking()
            .Where(s => storeId == null || s.StoreId == storeId)
            .MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (last is null)
            return [];

        var (start, end) = Period(from, to, last.Value);
        return await (
                from l in db.StoreSaleLines.AsNoTracking()
                join s in db.StoreSales on new { l.StoreId, Number = l.SaleNumber } equals new { s.StoreId, s.Number }
                join st in db.Stores on l.StoreId equals st.Id
                join p in db.Products on l.ProductId equals p.Id
                where l.Date >= start && l.Date <= end && (storeId == null || l.StoreId == storeId)
                orderby l.Date descending, st.Code, l.SaleNumber, l.Amount descending
                select new SaleLineExport(
                    l.Date, st.Code, st.Name, l.SaleNumber, s.RegisteredCustomer, s.FiscalDocument, s.Payment, s.Net,
                    p.Code, p.Description, l.Quantity, l.UnitPrice, l.Amount))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Período do relatório: sem datas, os últimos 30 dias. Depois do último dia importado não há venda: o período termina nele,
    /// senão a comparação com o período anterior (que tem o mesmo número de dias) mostraria quedas que não existem.
    /// </summary>
    private static (DateOnly Start, DateOnly End) Period(DateOnly? from, DateOnly? to, DateOnly last)
    {
        var end = to is { } requested && requested < last ? requested : last;
        var start = from ?? end.AddDays(-(DefaultDays - 1));
        if (start > end) start = end;
        if (end.DayNumber - start.DayNumber + 1 > MaxDays) start = end.AddDays(-(MaxDays - 1));
        return (start, end);
    }

    /// <summary>Produtos vendidos no período (relatório detalhado por itens), dos que mais venderam em valor.</summary>
    private async Task<DailySalesProducts> ProductsAsync(
        DateOnly start, DateOnly end, int? storeId, int? limit, CancellationToken cancellationToken)
    {
        var grouped = db.DailySaleItems.AsNoTracking()
            .Where(i => i.Date >= start && i.Date <= end && (storeId == null || i.StoreId == storeId))
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Quantity = g.Sum(i => i.Quantity),
                Amount = g.Sum(i => i.Amount),
                Days = g.Select(i => i.Date).Distinct().Count(),
                Stores = g.Select(i => i.StoreId).Distinct().Count()
            });
        var total = await grouped.CountAsync(cancellationToken);
        if (total == 0)
            return new DailySalesProducts(false, 0, []);

        var ordered = grouped.OrderByDescending(g => g.Amount).ThenBy(g => g.ProductId);
        var rows = await (
                from g in limit is null ? ordered : ordered.Take(limit.Value)
                join p in db.Products on g.ProductId equals p.Id
                select new DailySalesProduct(g.ProductId, p.Code, p.Description, p.Brand.Name, g.Quantity, g.Amount, g.Days, g.Stores))
            .ToListAsync(cancellationToken);
        return new DailySalesProducts(true, total, rows.OrderByDescending(r => r.Amount).ToList());
    }

    private static DailySalesTotals Totals(List<Row> rows)
    {
        var net = rows.Sum(r => r.Net);
        var sales = rows.Sum(r => r.Sales);
        var pieces = rows.Sum(r => r.Pieces);
        var days = rows.Select(r => r.Date).Distinct().Count();
        return new DailySalesTotals(
            net, rows.Sum(r => r.Gross), sales, pieces, days,
            sales == 0 ? null : Math.Round(net / sales, 2),
            sales == 0 ? null : Math.Round(pieces / sales, 2),
            days == 0 ? null : Math.Round(net / days, 2));
    }

    /// <summary>A rede (ou a loja) dia a dia: soma das lojas em cada data.</summary>
    private static List<(DateOnly Date, decimal Net, int Sales, decimal Pieces)> ByDay(List<Row> rows) =>
        rows.GroupBy(r => r.Date)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(r => r.Net), g.Sum(r => r.Sales), g.Sum(r => r.Pieces)))
            .ToList();

    private static List<DailySalesStore> Stores(List<Row> current, List<Row> previous) =>
        current.GroupBy(r => (r.StoreId, r.Code, r.Name))
            .Select(g =>
            {
                var net = g.Sum(r => r.Net);
                var sales = g.Sum(r => r.Sales);
                var best = g.MaxBy(r => r.Net)!;
                var before = previous.Where(r => r.StoreId == g.Key.StoreId).ToList();
                return new DailySalesStore(
                    g.Key.StoreId, g.Key.Code, g.Key.Name, net, sales, g.Sum(r => r.Pieces), g.Count(),
                    sales == 0 ? null : Math.Round(net / sales, 2),
                    before.Count == 0 ? null : before.Sum(r => r.Net),
                    best.Date, best.Net);
            })
            .OrderByDescending(s => s.Net)
            .ToList();

    private static List<DailySalesWeekday> Weekdays(List<Row> current)
    {
        var days = ByDay(current);
        return Enumerable.Range(0, 7)
            .Select(d =>
            {
                var matching = days.Where(x => (int)x.Date.DayOfWeek == d).ToList();
                return new DailySalesWeekday(
                    d, WeekdayLabels[d],
                    matching.Count == 0 ? 0 : Math.Round(matching.Average(x => x.Net), 2),
                    matching.Count == 0 ? 0 : Math.Round((decimal)matching.Average(x => x.Sales), 1),
                    matching.Count);
            })
            .ToList();
    }

    private sealed record Row(int StoreId, string Code, string Name, DateOnly Date, int Sales, decimal Pieces, decimal Gross, decimal Net);
}
