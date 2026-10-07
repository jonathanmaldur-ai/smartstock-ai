using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Analysis;
using SmartStock.Application.Common;
using SmartStock.Domain.Analysis;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

internal sealed class AnalysisQueryService(SmartStockDbContext db) : IAnalysisQueryService
{
    private const int MaxPageSize = 100;

    public async Task<PagedResult<PositionDto>> ListPositionsAsync(PositionQuery query, CancellationToken cancellationToken = default)
    {
        var analysisId = await LatestAnalysisIdAsync(cancellationToken);
        var (page, pageSize) = Paging(query.Page, query.PageSize);
        if (analysisId is null)
            return new PagedResult<PositionDto>([], page, pageSize, 0);

        var positions =
            from p in db.StockPositions.AsNoTracking()
            join product in db.Products on p.ProductId equals product.Id
            join store in db.Stores on p.StoreId equals store.Id
            where p.AnalysisId == analysisId
            select new { p, product, store };

        if (query.Situation is not null) positions = positions.Where(x => x.p.Situation == query.Situation);
        if (query.StoreId is not null) positions = positions.Where(x => x.p.StoreId == query.StoreId);
        if (query.BrandId is not null) positions = positions.Where(x => x.product.BrandId == query.BrandId);
        if (query.CategoryId is not null) positions = positions.Where(x => x.product.CategoryId == query.CategoryId);
        if (Pattern(query.Search) is { } pattern)
            positions = positions.Where(x => EF.Functions.ILike(x.product.Code, pattern) || EF.Functions.ILike(x.product.Description, pattern)
                                             || (x.product.Reference != null && EF.Functions.ILike(x.product.Reference, pattern)));

        var total = await positions.CountAsync(cancellationToken);
        var items = await positions
            .OrderByDescending(x => x.p.Priority).ThenByDescending(x => x.p.Sold12Months).ThenBy(x => x.product.Description)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PositionDto(
                x.product.Id, x.product.Code, x.product.Description, x.product.Brand.Name, x.product.Category.Name,
                x.store.Code, x.store.Name, x.p.Stock, x.p.ProjectedStock, x.p.Sold12Months, x.p.DailyAverage, x.p.CoverageDays,
                x.p.Situation, x.p.Priority))
            .ToListAsync(cancellationToken);

        return new PagedResult<PositionDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<SuggestionDto>> ListSuggestionsAsync(SuggestionQuery query, CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = Paging(query.Page, query.PageSize);
        var filtered = FilterSuggestions(query);
        var total = await filtered.CountAsync(cancellationToken);
        var items = await ProjectSuggestions(filtered).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<SuggestionDto>(items, page, pageSize, total);
    }

    /// <summary>Rotas com mais itens críticos primeiro; depois as de mais unidades.</summary>
    public async Task<IReadOnlyList<SuggestionRouteDto>> ListRoutesAsync(SuggestionQuery query, CancellationToken cancellationToken = default)
    {
        var groups = await FilterSuggestions(query)
            .GroupBy(s => new { s.OriginStoreId, s.DestinationStoreId })
            .Select(g => new
            {
                g.Key.OriginStoreId,
                g.Key.DestinationStoreId,
                Count = g.Count(),
                Units = g.Sum(s => s.Quantity),
                Critical = g.Count(s => s.Priority == AlertPriority.Critical),
                Negative = g.Count(s => s.DestinationNegative)
            })
            .ToListAsync(cancellationToken);

        var stores = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        var previews = await RoutePreviewsAsync(query, cancellationToken);
        return groups
            .Select(g => new SuggestionRouteDto(
                g.OriginStoreId, stores[g.OriginStoreId].Code, stores[g.OriginStoreId].Name,
                g.DestinationStoreId, stores[g.DestinationStoreId].Code, stores[g.DestinationStoreId].Name,
                g.Count, g.Units, g.Critical, g.Negative,
                previews.GetValueOrDefault((g.OriginStoreId, g.DestinationStoreId)) ?? []))
            .OrderByDescending(r => r.CriticalCount).ThenByDescending(r => r.Units)
            .ToList();
    }

    private const int PreviewSize = 3;

    /// <summary>Os produtos mais urgentes de cada rota (mesma ordem da lista da rota).</summary>
    private async Task<Dictionary<(int, int), IReadOnlyList<RouteProductPreview>>> RoutePreviewsAsync(
        SuggestionQuery query, CancellationToken cancellationToken)
    {
        var rows = await (
                from s in FilterSuggestions(query)
                join p in db.Products on s.ProductId equals p.Id
                select new { s.OriginStoreId, s.DestinationStoreId, s.Priority, s.DestinationCoverageDays, s.Quantity, p.Description })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.OriginStoreId, r.DestinationStoreId))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RouteProductPreview>)g
                    .OrderByDescending(r => r.Priority).ThenBy(r => r.DestinationCoverageDays).ThenByDescending(r => r.Quantity)
                    .Take(PreviewSize)
                    .Select(r => new RouteProductPreview(r.Description, r.Quantity))
                    .ToList());
    }

    public async Task<IReadOnlyList<SuggestionDto>> ListAllSuggestionsAsync(SuggestionQuery query, CancellationToken cancellationToken = default) =>
        await ProjectSuggestions(FilterSuggestions(query)).ToListAsync(cancellationToken);

    public async Task<PagedResult<PurchaseDto>> ListPurchasesAsync(PurchaseQuery query, CancellationToken cancellationToken = default)
    {
        var analysisId = await LatestAnalysisIdAsync(cancellationToken);
        var (page, pageSize) = Paging(query.Page, query.PageSize);
        if (analysisId is null)
            return new PagedResult<PurchaseDto>([], page, pageSize, 0);

        var purchases =
            from p in db.PurchaseSuggestions.AsNoTracking()
            join product in db.Products on p.ProductId equals product.Id
            join store in db.Stores on p.StoreId equals store.Id
            where p.AnalysisId == analysisId
            select new { p, product, store };

        if (query.StoreId is not null) purchases = purchases.Where(x => x.p.StoreId == query.StoreId);
        if (query.BrandId is not null) purchases = purchases.Where(x => x.product.BrandId == query.BrandId);
        if (query.CategoryId is not null) purchases = purchases.Where(x => x.product.CategoryId == query.CategoryId);
        if (Pattern(query.Search) is { } pattern)
            purchases = purchases.Where(x => EF.Functions.ILike(x.product.Code, pattern) || EF.Functions.ILike(x.product.Description, pattern));

        var total = await purchases.CountAsync(cancellationToken);
        var items = await purchases
            .OrderByDescending(x => x.p.DailyAverage).ThenBy(x => x.product.Description)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PurchaseDto(
                x.product.Id, x.product.Code, x.product.Description, x.product.Brand.Name, x.product.Category.Name,
                x.store.Code, x.store.Name, x.p.Quantity, x.p.Stock, x.p.DailyAverage, x.p.CoverageDays))
            .ToListAsync(cancellationToken);

        return new PagedResult<PurchaseDto>(items, page, pageSize, total);
    }

    private IQueryable<TransferSuggestion> FilterSuggestions(SuggestionQuery query)
    {
        var suggestions = db.TransferSuggestions.AsNoTracking();
        if (query.Status is not null) suggestions = suggestions.Where(s => s.Status == query.Status);
        if (query.OriginStoreId is not null) suggestions = suggestions.Where(s => s.OriginStoreId == query.OriginStoreId);
        if (query.DestinationStoreId is not null) suggestions = suggestions.Where(s => s.DestinationStoreId == query.DestinationStoreId);
        if (query.Priority is not null) suggestions = suggestions.Where(s => s.Priority == query.Priority);
        if (query.HideNegativeDestination) suggestions = suggestions.Where(s => !s.DestinationNegative);
        if (query.BrandId is not null) suggestions = suggestions.Where(s => db.Products.Any(p => p.Id == s.ProductId && p.BrandId == query.BrandId));
        if (query.CategoryId is not null) suggestions = suggestions.Where(s => db.Products.Any(p => p.Id == s.ProductId && p.CategoryId == query.CategoryId));
        if (Pattern(query.Search) is { } pattern)
            suggestions = suggestions.Where(s => db.Products.Any(p => p.Id == s.ProductId &&
                (EF.Functions.ILike(p.Code, pattern) || EF.Functions.ILike(p.Description, pattern) ||
                 (p.Reference != null && EF.Functions.ILike(p.Reference, pattern)))));
        return suggestions;
    }

    /// <summary>Mais urgentes primeiro; dentro da mesma prioridade, o destino com menor cobertura e a maior quantidade.</summary>
    private IQueryable<SuggestionDto> ProjectSuggestions(IQueryable<TransferSuggestion> suggestions) =>
        from s in suggestions
        join product in db.Products on s.ProductId equals product.Id
        join origin in db.Stores on s.OriginStoreId equals origin.Id
        join destination in db.Stores on s.DestinationStoreId equals destination.Id
        join analysis in db.StockAnalyses on s.AnalysisId equals analysis.Id
        orderby s.Priority descending, s.DestinationCoverageDays, s.Quantity descending, s.Id
        select new SuggestionDto(
            s.Id, product.Id, product.Code, product.Description, product.Reference, product.Brand.Name, product.Category.Name,
            origin.Code, origin.Name, destination.Code, destination.Name, s.Quantity, s.Priority, s.Status, s.Reason,
            s.OriginStock, s.OriginDailyAverage, s.OriginCoverageDays,
            s.DestinationStock, s.DestinationDailyAverage, s.DestinationCoverageDays, s.DestinationCoverageAfter, s.DestinationNegative,
            analysis.AnalysisDate, s.DecidedAt, s.DecidedByEmail, s.DecisionNote, s.CompletedOn, s.CompletedQuantity);

    private Task<Guid?> LatestAnalysisIdAsync(CancellationToken cancellationToken) =>
        db.StockAnalyses.AsNoTracking().OrderByDescending(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);

    private static (int Page, int PageSize) Paging(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));

    private static string? Pattern(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
}
