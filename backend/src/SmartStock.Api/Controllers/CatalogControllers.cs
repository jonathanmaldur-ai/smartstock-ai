using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStock.Api.Common;
using SmartStock.Api.Security;
using SmartStock.Application.Catalog;
using SmartStock.Application.Common;
using SmartStock.Application.Inventory;

namespace SmartStock.Api.Controllers;

/// <summary>Consultas liberadas para todos os perfis; alterações só para o Administrador.</summary>
[ApiController]
[Route("api/stores")]
public sealed class StoresController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<StoreDto>> List(CancellationToken cancellationToken) => catalog.ListStoresAsync(cancellationToken);

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<StoreDto>> Update(int id, StoreBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.UpdateStoreAsync(id, body.Name, body.City, cancellationToken));
}

[ApiController]
[Route("api/brands")]
public sealed class BrandsController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<BrandDto>> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        catalog.ListBrandsAsync(search, page, pageSize, cancellationToken);
}

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken cancellationToken) => catalog.ListCategoriesAsync(cancellationToken);

    [HttpPut("{id:int}/exclusion")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<CategoryDto>> SetExclusion(int id, CategoryExclusionBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.SetCategoryExcludedAsync(id, body.Excluded, cancellationToken));

    [HttpPost("{id:int}/merge")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<CategoryDto>> Merge(int id, CategoryMergeBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.MergeCategoryAsync(id, body.TargetId, cancellationToken));
}

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProductDto>> List(
        [FromQuery] string? search,
        [FromQuery] int? brandId,
        [FromQuery] int? categoryId,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        catalog.ListProductsAsync(new ProductQuery(search, brandId, categoryId, isActive, page, pageSize), cancellationToken);

    /// <summary>Marca ou desmarca o produto como sazonal / fora de época: sai das sugestões (decisão 37).</summary>
    [HttpPut("{id:int}/seasonal")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<ProductDto>> SetSeasonal(int id, SeasonalBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await catalog.SetProductSeasonalAsync(id, body.Seasonal, cancellationToken));

    /// <summary>Ficha do produto: estoque, vendas, VMD e cobertura por loja e últimas transferências.</summary>
    [HttpGet("{id:int}/overview")]
    public async Task<ActionResult<ProductOverview>> Overview(
        int id, [FromServices] IInventoryQueryService inventory, CancellationToken cancellationToken) =>
        this.ToActionResult(await inventory.GetProductOverviewAsync(id, cancellationToken));
}

public sealed record StoreBody(
    [Required(ErrorMessage = "Informe o nome."), MaxLength(100)] string Name,
    [MaxLength(100)] string? City);

public sealed record CategoryExclusionBody(bool Excluded);

public sealed record SeasonalBody(bool Seasonal);

public sealed record CategoryMergeBody([Range(1, int.MaxValue)] int TargetId);
