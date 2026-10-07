using SmartStock.Application.Common;
using SmartStock.Domain.Analysis;

namespace SmartStock.Application.Analysis;

/// <summary>Painel de estoque negativo (decisões 26 e 38). Consulta: o SmartStock não corrige o estoque.</summary>
public interface INegativeStockQueryService
{
    Task<NegativeSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NegativeGroupDto>> RankingAsync(NegativeGrouping grouping, int? storeId, CancellationToken cancellationToken = default);
    Task<PagedResult<NegativeItemDto>> ListAsync(NegativeQuery query, CancellationToken cancellationToken = default);

    /// <summary>Todos os itens do filtro (sem paginação), para o Excel de correção no ERP.</summary>
    Task<IReadOnlyList<NegativeItemDto>> ListAllAsync(NegativeQuery query, CancellationToken cancellationToken = default);
}

/// <param name="PreviousStockDate">Foto de estoque anterior, usada na tendência; null se esta é a primeira.</param>
public sealed record NegativeSummary(
    Guid? AnalysisId,
    DateOnly? StockDate,
    DateOnly? PreviousStockDate,
    int Items,
    decimal Units,
    int CriticalItems,
    IReadOnlyList<NegativeStoreDto> Stores,
    IReadOnlyList<NegativeCauseCount> Causes);

/// <param name="PercentOfItems">Itens negativos ÷ itens com estoque diferente de zero na loja × 100.</param>
public sealed record NegativeStoreDto(
    int StoreId,
    string Code,
    string Name,
    int Items,
    decimal Units,
    int CriticalItems,
    decimal PercentOfItems,
    int? PreviousItems,
    decimal? PreviousUnits);

public sealed record NegativeCauseCount(NegativeCause Cause, int Items);

public enum NegativeGrouping
{
    Brand = 1,
    Category = 2
}

public sealed record NegativeGroupDto(int Id, string Name, int Items, decimal Units);

public sealed record NegativeQuery(
    int? StoreId,
    AlertPriority? Priority,
    NegativeCause? Cause,
    int? BrandId,
    int? CategoryId,
    string? Search,
    int Page = 1,
    int PageSize = 25);

public sealed record NegativeItemDto(
    int ProductId,
    string ProductCode,
    string ProductDescription,
    string? ProductReference,
    string BrandName,
    string CategoryName,
    string StoreCode,
    string StoreName,
    decimal Quantity,
    decimal Sold12Months,
    AlertPriority Priority,
    IReadOnlyList<NegativeCause> Causes,
    decimal PendingTransferUnits,
    string? DuplicateProductCode);
