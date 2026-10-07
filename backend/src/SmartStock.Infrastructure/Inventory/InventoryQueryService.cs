using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Catalog;
using SmartStock.Application.Common;
using SmartStock.Application.Inventory;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Inventory;

internal sealed class InventoryQueryService(SmartStockDbContext db) : IInventoryQueryService
{
    private const int RecentTransfersLimit = 30;

    public async Task<Result<ProductOverview>> GetProductOverviewAsync(int productId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new ProductDto(
                p.Id, p.Code, p.Description, p.Unit, p.Reference, p.IsActive,
                p.Brand.Code, p.Brand.Name, p.Category.Name, p.Category.ExcludedFromAnalysis,
                p.Subcategory != null ? p.Subcategory.Name : null, p.IsSeasonal))
            .SingleOrDefaultAsync(cancellationToken);
        if (product is null)
            return Error.NotFound("product.not_found", "Produto não encontrado.");

        var stockBatch = await CurrentImports.StockBatchAsync(db, cancellationToken);
        var stock = stockBatch is null
            ? new Dictionary<int, decimal>()
            : await db.StockLevels.AsNoTracking()
                .Where(s => s.ImportId == stockBatch.Id && s.ProductId == productId)
                .ToDictionaryAsync(s => s.StoreId, s => s.Quantity, cancellationToken);

        var salesBatches = await CurrentImports.SalesBatchesAsync(db, cancellationToken);
        var salesBatchIds = salesBatches.Values.Select(b => b.Id).ToList();
        var sales = await db.SalesTotals.AsNoTracking()
            .Where(s => salesBatchIds.Contains(s.ImportId) && s.ProductId == productId)
            .ToDictionaryAsync(s => s.StoreId, s => s.Quantity, cancellationToken);

        var stores = await db.Stores.AsNoTracking()
            .Where(s => s.Status == StoreStatus.Active)
            .OrderBy(s => s.Code)
            .ToListAsync(cancellationToken);

        var storeRows = stores
            .Select(s => BuildStoreRow(s, stockBatch is null ? null : (decimal?)stock.GetValueOrDefault(s.Id), salesBatches.GetValueOrDefault(s.Id), sales))
            .ToList();

        return new ProductOverview(product, stockBatch?.ReferenceDate, storeRows, await RecentTransfersAsync(productId, cancellationToken));
    }

    private static StoreInventory BuildStoreRow(Store store, decimal? stock, ImportBatch? salesBatch, Dictionary<int, decimal> sales)
    {
        if (salesBatch is null)
            return new StoreInventory(store.Code, store.Name, store.Type, stock, null, null, null, null);

        var sold = sales.GetValueOrDefault(store.Id);
        var dailyAverage = StockMetrics.DailyAverage(sold);
        return new StoreInventory(
            store.Code, store.Name, store.Type, stock, sold, salesBatch.PeriodEnd,
            dailyAverage, stock is null ? null : StockMetrics.CoverageDays(stock.Value, dailyAverage));
    }

    private async Task<IReadOnlyList<TransferMovementDto>> RecentTransfersAsync(int productId, CancellationToken cancellationToken)
    {
        var storeCodes = await db.Stores.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, cancellationToken);
        var movements = await db.TransferMovements.AsNoTracking()
            .Where(t => t.ProductId == productId)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Take(RecentTransfersLimit)
            .ToListAsync(cancellationToken);

        return movements
            .Select(t => new TransferMovementDto(
                t.Date, t.IsCancellation, storeCodes[t.OriginStoreId], storeCodes[t.DestinationStoreId], t.Direction, t.Quantity, t.UserName))
            .ToList();
    }
}
