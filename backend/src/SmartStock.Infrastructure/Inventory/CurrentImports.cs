using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Imports;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Inventory;

/// <summary>Quais importações valem como "dados atuais" (decisões 31 e 32).</summary>
internal static class CurrentImports
{
    /// <summary>Foto de estoque atual: a de data mais recente (em empate, a última confirmada).</summary>
    public static Task<ImportBatch?> StockBatchAsync(SmartStockDbContext db, CancellationToken cancellationToken) =>
        db.ImportBatches.AsNoTracking()
            .Where(b => b.Type == ImportType.Stock && b.Status == ImportStatus.Confirmed)
            .OrderByDescending(b => b.ReferenceDate).ThenByDescending(b => b.DecidedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Vendas atuais de cada loja (chave: StoreId): a importação de data mais recente da loja.</summary>
    public static async Task<Dictionary<int, ImportBatch>> SalesBatchesAsync(SmartStockDbContext db, CancellationToken cancellationToken)
    {
        var batches = await db.ImportBatches.AsNoTracking()
            .Where(b => b.Type == ImportType.Sales && b.Status == ImportStatus.Confirmed && b.StoreId != null)
            .ToListAsync(cancellationToken);

        return batches
            .GroupBy(b => b.StoreId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.ReferenceDate).ThenByDescending(b => b.DecidedAt).First());
    }
}
