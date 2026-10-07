using SmartStock.Application.Catalog;
using SmartStock.Application.Common;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Inventory;

namespace SmartStock.Application.Inventory;

/// <summary>Consultas sobre os dados importados de estoque, vendas e transferências (decisão 34).</summary>
public interface IInventoryQueryService
{
    /// <summary>Ficha do produto: estoque atual por loja, venda de 12 meses, VMD, cobertura e últimas transferências.</summary>
    Task<Result<ProductOverview>> GetProductOverviewAsync(int productId, CancellationToken cancellationToken = default);
}

/// <param name="StockDate">Data da foto de estoque atual; null se nenhum estoque foi importado.</param>
public sealed record ProductOverview(
    ProductDto Product,
    DateOnly? StockDate,
    IReadOnlyList<StoreInventory> Stores,
    IReadOnlyList<TransferMovementDto> RecentTransfers);

/// <param name="Stock">null quando não há estoque importado.</param>
/// <param name="Sold12Months">null quando as vendas da loja ainda não foram importadas.</param>
/// <param name="CoverageDays">null quando a loja não vende o produto.</param>
public sealed record StoreInventory(
    string StoreCode,
    string StoreName,
    StoreType StoreType,
    decimal? Stock,
    decimal? Sold12Months,
    DateOnly? SalesPeriodEnd,
    decimal? DailyAverage,
    decimal? CoverageDays);

public sealed record TransferMovementDto(
    DateOnly Date,
    bool IsCancellation,
    string OriginCode,
    string DestinationCode,
    TransferDirection Direction,
    decimal Quantity,
    string? UserName);
