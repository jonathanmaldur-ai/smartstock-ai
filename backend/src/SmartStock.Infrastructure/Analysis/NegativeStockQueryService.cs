using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Analysis;
using SmartStock.Application.Common;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

internal sealed class NegativeStockQueryService(SmartStockDbContext db) : INegativeStockQueryService
{
    private const int RankingSize = 20;
    private const int MaxPageSize = 100;

    private static readonly NegativeCause[] AllCauses =
    [
        NegativeCause.TransferNotReceived, NegativeCause.SaleWithoutEntry, NegativeCause.ShippedWithoutEntry,
        NegativeCause.FractionalUnit, NegativeCause.PossibleDuplicate
    ];

    public async Task<NegativeSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var analysis = await LatestAnalysisAsync(cancellationToken);
        if (analysis is null)
            return new NegativeSummary(null, null, null, 0, 0, 0, [], []);

        var negatives = db.NegativeStocks.AsNoTracking().Where(n => n.AnalysisId == analysis.Id);
        var byStore = await negatives
            .GroupBy(n => n.StoreId)
            .Select(g => new { StoreId = g.Key, Items = g.Count(), Units = g.Sum(n => n.Quantity), Critical = g.Count(n => n.Priority == AlertPriority.Critical) })
            .ToListAsync(cancellationToken);
        var causes = await negatives.Select(n => n.Causes).ToListAsync(cancellationToken);

        var itemsWithStock = await CountByStoreAsync(analysis.StockImportId, cancellationToken);
        var previous = await PreviousStockBatchAsync(analysis.StockImportId, cancellationToken);
        var previousNegatives = previous is null ? null : await NegativesByStoreAsync(previous.Id, cancellationToken);

        var stores = await db.Stores.AsNoTracking().Where(s => s.Status == StoreStatus.Active).OrderBy(s => s.Code).ToListAsync(cancellationToken);
        var storeRows = stores
            .Select(s =>
            {
                var current = byStore.SingleOrDefault(b => b.StoreId == s.Id);
                var total = itemsWithStock.GetValueOrDefault(s.Id);
                var before = previousNegatives?.GetValueOrDefault(s.Id);
                return new NegativeStoreDto(
                    s.Id, s.Code, s.Name, current?.Items ?? 0, current?.Units ?? 0, current?.Critical ?? 0,
                    total == 0 ? 0 : Math.Round(100m * (current?.Items ?? 0) / total, 1),
                    previousNegatives is null ? null : before?.Items ?? 0,
                    previousNegatives is null ? null : before?.Units ?? 0);
            })
            .ToList();

        return new NegativeSummary(
            analysis.Id, analysis.StockDate, previous?.ReferenceDate,
            byStore.Sum(b => b.Items), byStore.Sum(b => b.Units), byStore.Sum(b => b.Critical),
            storeRows,
            AllCauses.Select(c => new NegativeCauseCount(c, causes.Count(x => x.HasFlag(c)))).ToList());
    }

    public async Task<IReadOnlyList<NegativeGroupDto>> RankingAsync(NegativeGrouping grouping, int? storeId, CancellationToken cancellationToken = default)
    {
        var analysis = await LatestAnalysisAsync(cancellationToken);
        if (analysis is null)
            return [];

        var rows =
            from n in db.NegativeStocks.AsNoTracking()
            join p in db.Products on n.ProductId equals p.Id
            where n.AnalysisId == analysis.Id && (storeId == null || n.StoreId == storeId)
            select new { n.Quantity, GroupId = grouping == NegativeGrouping.Brand ? p.BrandId : p.CategoryId, GroupName = grouping == NegativeGrouping.Brand ? p.Brand.Name : p.Category.Name };

        var groups = await rows
            .GroupBy(r => new { r.GroupId, r.GroupName })
            .Select(g => new { g.Key.GroupId, g.Key.GroupName, Items = g.Count(), Units = g.Sum(r => r.Quantity) })
            .OrderBy(g => g.Units)
            .Take(RankingSize)
            .ToListAsync(cancellationToken);
        return groups.Select(g => new NegativeGroupDto(g.GroupId, g.GroupName, g.Items, g.Units)).ToList();
    }

    public async Task<PagedResult<NegativeItemDto>> ListAsync(NegativeQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var filtered = await FilterAsync(query, cancellationToken);
        if (filtered is null)
            return new PagedResult<NegativeItemDto>([], page, pageSize, 0);

        var total = await filtered.CountAsync(cancellationToken);
        var items = await Project(filtered).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<NegativeItemDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<IReadOnlyList<NegativeItemDto>> ListAllAsync(NegativeQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = await FilterAsync(query, cancellationToken);
        return filtered is null ? [] : (await Project(filtered).ToListAsync(cancellationToken)).Select(ToDto).ToList();
    }

    private async Task<IQueryable<NegativeStock>?> FilterAsync(NegativeQuery query, CancellationToken cancellationToken)
    {
        var analysis = await LatestAnalysisAsync(cancellationToken);
        if (analysis is null)
            return null;

        var negatives = db.NegativeStocks.AsNoTracking().Where(n => n.AnalysisId == analysis.Id);
        if (query.StoreId is not null) negatives = negatives.Where(n => n.StoreId == query.StoreId);
        if (query.Priority is not null) negatives = negatives.Where(n => n.Priority == query.Priority);
        if (query.Cause is { } cause) negatives = negatives.Where(n => ((int)n.Causes & (int)cause) != 0);
        if (query.BrandId is not null) negatives = negatives.Where(n => db.Products.Any(p => p.Id == n.ProductId && p.BrandId == query.BrandId));
        if (query.CategoryId is not null) negatives = negatives.Where(n => db.Products.Any(p => p.Id == n.ProductId && p.CategoryId == query.CategoryId));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            negatives = negatives.Where(n => db.Products.Any(p => p.Id == n.ProductId &&
                (EF.Functions.ILike(p.Code, pattern) || EF.Functions.ILike(p.Description, pattern) ||
                 (p.Reference != null && EF.Functions.ILike(p.Reference, pattern)))));
        }
        return negatives;
    }

    /// <summary>Mais urgentes e maiores primeiro.</summary>
    private IQueryable<NegativeRow> Project(IQueryable<NegativeStock> negatives) =>
        from n in negatives
        join p in db.Products on n.ProductId equals p.Id
        join s in db.Stores on n.StoreId equals s.Id
        orderby n.Priority descending, n.Quantity
        select new NegativeRow(
            p.Id, p.Code, p.Description, p.Reference, p.Brand.Name, p.Category.Name, s.Code, s.Name,
            n.Quantity, n.Sold12Months, n.Priority, n.Causes, n.PendingTransferUnits, n.DuplicateProductCode);

    private static NegativeItemDto ToDto(NegativeRow r) =>
        new(r.ProductId, r.ProductCode, r.ProductDescription, r.ProductReference, r.BrandName, r.CategoryName, r.StoreCode, r.StoreName,
            r.Quantity, r.Sold12Months, r.Priority, AllCauses.Where(c => r.Causes.HasFlag(c)).ToList(), r.PendingTransferUnits, r.DuplicateProductCode);

    private Task<StockAnalysis?> LatestAnalysisAsync(CancellationToken cancellationToken) =>
        db.StockAnalyses.AsNoTracking().OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    /// <summary>A foto de estoque confirmada imediatamente anterior à usada na análise.</summary>
    private async Task<ImportBatch?> PreviousStockBatchAsync(Guid currentStockImportId, CancellationToken cancellationToken)
    {
        var current = await db.ImportBatches.AsNoTracking().SingleAsync(b => b.Id == currentStockImportId, cancellationToken);
        return await db.ImportBatches.AsNoTracking()
            .Where(b => b.Type == ImportType.Stock && b.Status == ImportStatus.Confirmed && b.Id != current.Id &&
                        (b.ReferenceDate < current.ReferenceDate || (b.ReferenceDate == current.ReferenceDate && b.DecidedAt < current.DecidedAt)))
            .OrderByDescending(b => b.ReferenceDate).ThenByDescending(b => b.DecidedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Itens (produto-loja) da foto de estoque por loja, só lojas ativas e categorias analisadas.</summary>
    private async Task<Dictionary<int, int>> CountByStoreAsync(Guid stockImportId, CancellationToken cancellationToken) =>
        await AnalysedStock(stockImportId)
            .GroupBy(l => l.StoreId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    private async Task<Dictionary<int, (int Items, decimal Units)>> NegativesByStoreAsync(Guid stockImportId, CancellationToken cancellationToken) =>
        (await AnalysedStock(stockImportId)
            .Where(l => l.Quantity < 0)
            .GroupBy(l => l.StoreId)
            .Select(g => new { g.Key, Count = g.Count(), Units = g.Sum(l => l.Quantity) })
            .ToListAsync(cancellationToken))
        .ToDictionary(g => g.Key, g => (g.Count, g.Units));

    private IQueryable<Domain.Inventory.StockLevel> AnalysedStock(Guid stockImportId) =>
        from l in db.StockLevels.AsNoTracking()
        join p in db.Products on l.ProductId equals p.Id
        join s in db.Stores on l.StoreId equals s.Id
        where l.ImportId == stockImportId && !p.Category.ExcludedFromAnalysis && s.Status == StoreStatus.Active
        select l;

    private sealed record NegativeRow(
        int ProductId, string ProductCode, string ProductDescription, string? ProductReference, string BrandName, string CategoryName,
        string StoreCode, string StoreName, decimal Quantity, decimal Sold12Months, AlertPriority Priority, NegativeCause Causes,
        decimal PendingTransferUnits, string? DuplicateProductCode);
}
