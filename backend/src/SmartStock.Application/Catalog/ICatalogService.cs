using SmartStock.Application.Common;
using SmartStock.Domain.Catalog;

namespace SmartStock.Application.Catalog;

public interface ICatalogService
{
    Task<IReadOnlyList<StoreDto>> ListStoresAsync(CancellationToken cancellationToken = default);
    Task<Result<StoreDto>> UpdateStoreAsync(int id, string name, string? city, CancellationToken cancellationToken = default);

    Task<PagedResult<BrandDto>> ListBrandsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken cancellationToken = default);
    Task<Result<CategoryDto>> SetCategoryExcludedAsync(int id, bool excluded, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unifica duas categorias (tabela de-para): a origem vira grafia alternativa do destino
    /// e os produtos dela passam para o destino.
    /// </summary>
    Task<Result<CategoryDto>> MergeCategoryAsync(int sourceId, int targetId, CancellationToken cancellationToken = default);

    Task<PagedResult<ProductDto>> ListProductsAsync(ProductQuery query, CancellationToken cancellationToken = default);

    /// <summary>Marca ou desmarca o produto como sazonal / fora de época (decisão 37).</summary>
    Task<Result<ProductDto>> SetProductSeasonalAsync(int id, bool seasonal, CancellationToken cancellationToken = default);
}

public sealed record StoreDto(int Id, string Code, string Name, string? City, StoreType Type, StoreStatus Status);

public sealed record BrandDto(int Id, string Code, string Name, bool IsActive, int ProductCount);

public sealed record CategoryDto(
    int Id,
    string Name,
    bool ExcludedFromAnalysis,
    int ProductCount,
    IReadOnlyList<string> Aliases);

public sealed record ProductDto(
    int Id,
    string Code,
    string Description,
    string? Unit,
    string? Reference,
    bool IsActive,
    string BrandCode,
    string BrandName,
    string CategoryName,
    bool CategoryExcluded,
    string? SubcategoryName,
    bool IsSeasonal);

public sealed record ProductQuery(
    string? Search = null,
    int? BrandId = null,
    int? CategoryId = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 25)
{
    public const int MaxPageSize = 100;
}
