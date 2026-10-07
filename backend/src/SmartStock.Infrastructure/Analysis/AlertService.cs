using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Analysis;
using SmartStock.Application.Common;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Auditing;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

/// <summary>Alertas da última análise (decisão 47).</summary>
internal sealed class AlertService(SmartStockDbContext db, IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock) : IAlertService
{
    private const int MaxPageSize = 100;
    private const int MaxSeenPerRequest = 5000;

    public async Task<AlertSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var analysis = await LatestAnalysisAsync(cancellationToken);
        if (analysis is null)
            return new AlertSummary(null, null, null, []);

        var previousStockDate = await db.StockAnalyses.AsNoTracking()
            .Where(a => a.StockDate < analysis.StockDate)
            .MaxAsync(a => (DateOnly?)a.StockDate, cancellationToken);
        var counts = await db.StockAlerts.AsNoTracking()
            .Where(a => a.AnalysisId == analysis.Id)
            .GroupBy(a => a.Type)
            .Select(g => new { Type = g.Key, Priority = g.Max(a => a.Priority), Total = g.Count(), Unseen = g.Count(a => a.SeenAt == null) })
            .ToListAsync(cancellationToken);

        var types = Enum.GetValues<AlertType>()
            .Select(t =>
            {
                var c = counts.SingleOrDefault(x => x.Type == t);
                return new AlertTypeCount(t, AlertTypeLabels.Of(t), c?.Priority ?? DefaultPriority(t), c?.Total ?? 0, c?.Unseen ?? 0);
            })
            .ToList();
        return new AlertSummary(analysis.Id, analysis.StockDate, previousStockDate, types);
    }

    public async Task<PagedResult<AlertDto>> ListAsync(AlertQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var analysis = await LatestAnalysisAsync(cancellationToken);
        if (analysis is null)
            return new PagedResult<AlertDto>([], page, pageSize, 0);

        var alerts = Filter(db.StockAlerts.AsNoTracking().Where(a => a.AnalysisId == analysis.Id), query);
        var total = await alerts.CountAsync(cancellationToken);
        var rows = await (
                from a in alerts
                join p in db.Products on a.ProductId equals p.Id into products
                from p in products.DefaultIfEmpty()
                join s in db.Stores on a.StoreId equals s.Id into stores
                from s in stores.DefaultIfEmpty()
                join b in db.Brands on a.BrandId equals b.Id into brands
                from b in brands.DefaultIfEmpty()
                orderby a.SeenAt != null, a.Priority descending, a.Type, s.Code, a.Id
                select new
                {
                    a.Id, a.Type, a.Priority, a.ProductId, ProductCode = p.Code, ProductDescription = p.Description,
                    StoreCode = s.Code, StoreName = s.Name, BrandName = b != null ? b.Name : p.Brand.Name,
                    a.Message, a.SeenAt, a.SeenByEmail
                })
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new AlertDto(
                r.Id, r.Type, AlertTypeLabels.Of(r.Type), r.Priority, r.ProductId, r.ProductCode, r.ProductDescription,
                r.StoreCode, r.StoreName, r.BrandName, r.Message, r.SeenAt, r.SeenByEmail))
            .ToList();
        return new PagedResult<AlertDto>(items, page, pageSize, total);
    }

    public async Task<Result<int>> MarkSeenAsync(IReadOnlyList<long> ids, bool seen, CancellationToken cancellationToken = default)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
            return Error.Validation("alert.none_selected", "Selecione pelo menos um alerta.");
        if (distinct.Count > MaxSeenPerRequest)
            return Error.Validation("alert.too_many", $"Marque no máximo {MaxSeenPerRequest} alertas por vez.");

        var now = clock.GetUtcNow();
        var email = currentUser.Email;
        var changed = seen
            ? await db.StockAlerts.Where(a => distinct.Contains(a.Id) && a.SeenAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.SeenAt, now).SetProperty(a => a.SeenByEmail, email), cancellationToken)
            : await db.StockAlerts.Where(a => distinct.Contains(a.Id) && a.SeenAt != null)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.SeenAt, (DateTimeOffset?)null).SetProperty(a => a.SeenByEmail, (string?)null), cancellationToken);

        await audit.LogAsync(new AuditEntry(
            seen ? AuditActions.AlertsSeen : AuditActions.AlertsUnseen, AuditResult.Success, "StockAlert",
            distinct.Count == 1 ? distinct[0].ToString() : null,
            Details: new { count = changed, ids = distinct.Take(100) }), cancellationToken);
        return changed;
    }

    private IQueryable<StockAlert> Filter(IQueryable<StockAlert> alerts, AlertQuery query)
    {
        if (!query.IncludeSeen) alerts = alerts.Where(a => a.SeenAt == null);
        if (query.Type is not null) alerts = alerts.Where(a => a.Type == query.Type);
        if (query.Priority is not null) alerts = alerts.Where(a => a.Priority == query.Priority);
        if (query.StoreId is not null) alerts = alerts.Where(a => a.StoreId == query.StoreId);
        if (query.BrandId is not null)
            alerts = alerts.Where(a => a.BrandId == query.BrandId || db.Products.Any(p => p.Id == a.ProductId && p.BrandId == query.BrandId));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            alerts = alerts.Where(a => db.Products.Any(p => p.Id == a.ProductId &&
                (EF.Functions.ILike(p.Code, pattern) || EF.Functions.ILike(p.Description, pattern) ||
                 (p.Reference != null && EF.Functions.ILike(p.Reference, pattern)))));
        }
        return alerts;
    }

    private static AlertPriority DefaultPriority(AlertType type) => type switch
    {
        AlertType.PredictedRupture or AlertType.SoldWithoutReplenishment => AlertPriority.High,
        AlertType.BrandConcentration => AlertPriority.Low,
        _ => AlertPriority.Medium
    };

    private Task<StockAnalysis?> LatestAnalysisAsync(CancellationToken cancellationToken) =>
        db.StockAnalyses.AsNoTracking().OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(cancellationToken);
}
