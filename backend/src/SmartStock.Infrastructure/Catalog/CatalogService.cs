using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Catalog;
using SmartStock.Application.Common;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Catalog;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Catalog;

internal sealed class CatalogService(SmartStockDbContext db, IAuditLogger audit) : ICatalogService
{
    private static readonly Error CategoryNotFound = Error.NotFound("category.not_found", "Categoria não encontrada.");

    public async Task<IReadOnlyList<StoreDto>> ListStoresAsync(CancellationToken cancellationToken = default) =>
        await db.Stores.AsNoTracking()
            .OrderBy(s => s.Status).ThenBy(s => s.Code)
            .Select(s => new StoreDto(s.Id, s.Code, s.Name, s.City, s.Type, s.Status))
            .ToListAsync(cancellationToken);

    public async Task<Result<StoreDto>> UpdateStoreAsync(int id, string name, string? city, CancellationToken cancellationToken = default)
    {
        var store = await db.Stores.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (store is null)
            return Error.NotFound("store.not_found", "Unidade não encontrada.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            return Error.Validation("store.invalid_name", "Informe o nome da unidade (até 100 caracteres).");
        if (city is { Length: > 100 })
            return Error.Validation("store.invalid_city", "A cidade pode ter até 100 caracteres.");

        var before = new { store.Name, store.City };
        store.Rename(name, city);
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.StoreUpdated, AuditResult.Success, "Store", store.Code,
            Details: new { before, after = new { store.Name, store.City } }), cancellationToken);

        return new StoreDto(store.Id, store.Code, store.Name, store.City, store.Type, store.Status);
    }

    public async Task<PagedResult<BrandDto>> ListBrandsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);
        var query = db.Brands.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b => EF.Functions.ILike(b.Name, $"%{term}%") || b.Code.StartsWith(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(b => b.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new BrandDto(b.Id, b.Code, b.Name, b.IsActive, db.Products.Count(p => p.BrandId == b.Id)))
            .ToListAsync(cancellationToken);

        return new PagedResult<BrandDto>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await db.Categories.AsNoTracking()
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.ExcludedFromAnalysis,
                ProductCount = db.Products.Count(p => p.CategoryId == c.Id),
                Aliases = c.Aliases.Select(a => a.Alias).OrderBy(a => a).ToList()
            })
            .ToListAsync(cancellationToken);

        return categories
            .OrderByDescending(c => c.ProductCount).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.ExcludedFromAnalysis, c.ProductCount, c.Aliases))
            .ToList();
    }

    public async Task<Result<CategoryDto>> SetCategoryExcludedAsync(int id, bool excluded, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
            return CategoryNotFound;

        if (category.ExcludedFromAnalysis != excluded)
        {
            category.ExcludedFromAnalysis = excluded;
            await db.SaveChangesAsync(cancellationToken);
            await audit.LogAsync(new AuditEntry(
                AuditActions.CategoryExclusionChanged, AuditResult.Success, "Category", category.Name,
                Details: new { category = category.Name, excludedFromAnalysis = excluded }), cancellationToken);
        }

        return await GetCategoryAsync(id, cancellationToken);
    }

    public async Task<Result<CategoryDto>> MergeCategoryAsync(int sourceId, int targetId, CancellationToken cancellationToken = default)
    {
        if (sourceId == targetId)
            return Error.Validation("category.merge_same", "Escolha uma categoria diferente para unificar.");

        var source = await db.Categories.Include(c => c.Aliases).SingleOrDefaultAsync(c => c.Id == sourceId, cancellationToken);
        var target = await db.Categories.Include(c => c.Aliases).SingleOrDefaultAsync(c => c.Id == targetId, cancellationToken);
        if (source is null || target is null)
            return CategoryNotFound;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var movedProducts = await db.Products
            .Where(p => p.CategoryId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, target.Id), cancellationToken);

        // O nome da categoria de origem e as grafias dela passam a apontar para o destino nas próximas importações.
        var aliases = source.Aliases.Select(a => a.Alias).Append(CatalogText.Key(source.Name))
            .Where(a => a != CatalogText.Key(target.Name) && target.Aliases.All(t => t.Alias != a))
            .Distinct()
            .ToList();

        db.Categories.Remove(source);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var alias in aliases)
            target.Aliases.Add(new CategoryAlias { Alias = alias });
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.CategoryMerged, AuditResult.Success, "Category", target.Name,
            Details: new { from = source.Name, into = target.Name, movedProducts, aliasesAdded = aliases }), cancellationToken);

        return await GetCategoryAsync(target.Id, cancellationToken);
    }

    public async Task<PagedResult<ProductDto>> ListProductsAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = NormalizePaging(query.Page, query.PageSize);
        var products = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            products = products.Where(p =>
                p.Code.StartsWith(term) ||
                EF.Functions.ILike(p.Description, $"%{term}%") ||
                (p.Reference != null && EF.Functions.ILike(p.Reference, $"{term}%")));
        }

        if (query.BrandId is not null)
            products = products.Where(p => p.BrandId == query.BrandId);
        if (query.CategoryId is not null)
            products = products.Where(p => p.CategoryId == query.CategoryId);
        if (query.IsActive is not null)
            products = products.Where(p => p.IsActive == query.IsActive);

        var total = await products.CountAsync(cancellationToken);
        var items = await products
            .OrderBy(p => p.Description).ThenBy(p => p.Code)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new ProductDto(
                p.Id, p.Code, p.Description, p.Unit, p.Reference, p.IsActive,
                p.Brand.Code, p.Brand.Name, p.Category.Name, p.Category.ExcludedFromAnalysis,
                p.Subcategory != null ? p.Subcategory.Name : null, p.IsSeasonal))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductDto>(items, page, pageSize, total);
    }

    public async Task<Result<ProductDto>> SetProductSeasonalAsync(int id, bool seasonal, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
            return Error.NotFound("product.not_found", "Produto não encontrado.");

        if (product.IsSeasonal != seasonal)
        {
            product.IsSeasonal = seasonal;
            await db.SaveChangesAsync(cancellationToken);
            await audit.LogAsync(new AuditEntry(
                AuditActions.ProductSeasonalChanged, AuditResult.Success, "Product", product.Code,
                Details: new { product = product.Code, product.Description, seasonal }), cancellationToken);
        }

        return await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDto(
                p.Id, p.Code, p.Description, p.Unit, p.Reference, p.IsActive,
                p.Brand.Code, p.Brand.Name, p.Category.Name, p.Category.ExcludedFromAnalysis,
                p.Subcategory != null ? p.Subcategory.Name : null, p.IsSeasonal))
            .SingleAsync(cancellationToken);
    }

    private async Task<Result<CategoryDto>> GetCategoryAsync(int id, CancellationToken cancellationToken)
    {
        var category = (await ListCategoriesAsync(cancellationToken)).SingleOrDefault(c => c.Id == id);
        return category is null ? CategoryNotFound : category;
    }

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, ProductQuery.MaxPageSize));
}
