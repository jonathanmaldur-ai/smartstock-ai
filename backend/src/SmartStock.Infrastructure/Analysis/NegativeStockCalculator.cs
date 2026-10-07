using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

/// <summary>
/// Classifica os estoques negativos de uma análise (decisão 38), cruzando estoque, vendas,
/// transferências importadas e o cadastro (unidade, marca e referência).
/// </summary>
internal sealed class NegativeStockCalculator(SmartStockDbContext db)
{
    public sealed record StockEntry(int ProductId, int StoreId, bool IsWarehouse, decimal Stock, decimal Sold12Months);

    public async Task<List<NegativeStock>> CalculateAsync(
        Guid analysisId, IReadOnlyList<StockEntry> entries, CancellationToken cancellationToken)
    {
        var negatives = entries.Where(e => e.Stock < 0).ToList();
        if (negatives.Count == 0)
            return [];

        var transfers = await LoadTransfersAsync(cancellationToken);
        var duplicates = await DuplicateFinderAsync(entries, negatives, cancellationToken);
        var units = await db.Products.AsNoTracking()
            .Where(p => p.Unit != null)
            .Select(p => new { p.Id, p.Unit })
            .ToDictionaryAsync(p => p.Id, p => p.Unit, cancellationToken);

        return negatives
            .Select(e =>
            {
                var toStore = transfers.ToStore.GetValueOrDefault((e.ProductId, e.StoreId));
                var facts = new NegativeFacts(
                    e.Stock, e.Sold12Months, e.IsWarehouse, units.GetValueOrDefault(e.ProductId),
                    Math.Max(toStore.Sent - toStore.Received - toStore.Canceled, 0),
                    toStore.Received > 0,
                    transfers.FromStore.GetValueOrDefault((e.ProductId, e.StoreId)) > 0,
                    duplicates(e));
                return new NegativeStock
                {
                    AnalysisId = analysisId,
                    ProductId = e.ProductId,
                    StoreId = e.StoreId,
                    Quantity = e.Stock,
                    Sold12Months = e.Sold12Months,
                    Priority = NegativeRules.Priority(facts),
                    Causes = NegativeRules.Causes(facts),
                    PendingTransferUnits = facts.PendingTransferUnits,
                    DuplicateProductCode = facts.DuplicateProductCode
                };
            })
            .ToList();
    }

    /// <summary>
    /// Para a loja como destino: enviado (saída na origem), recebido (entrada) e cancelado (entrada de cancelamento na rota).
    /// Para a loja como origem: enviado.
    /// </summary>
    private async Task<TransferTotals> LoadTransfersAsync(CancellationToken cancellationToken)
    {
        var toStore = await db.TransferMovements.AsNoTracking()
            .GroupBy(m => new { m.ProductId, m.DestinationStoreId })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.DestinationStoreId,
                Sent = g.Where(m => m.Direction == TransferDirection.Out && !m.IsCancellation).Sum(m => m.Quantity),
                Received = g.Where(m => m.Direction == TransferDirection.In && !m.IsCancellation).Sum(m => m.Quantity),
                Canceled = g.Where(m => m.Direction == TransferDirection.In && m.IsCancellation).Sum(m => m.Quantity)
            })
            .ToListAsync(cancellationToken);

        var fromStore = await db.TransferMovements.AsNoTracking()
            .Where(m => m.Direction == TransferDirection.Out && !m.IsCancellation)
            .GroupBy(m => new { m.ProductId, m.OriginStoreId })
            .Select(g => new { g.Key.ProductId, g.Key.OriginStoreId, Sent = g.Sum(m => m.Quantity) })
            .ToListAsync(cancellationToken);

        return new TransferTotals(
            toStore.ToDictionary(t => (t.ProductId, t.DestinationStoreId), t => (t.Sent, t.Received, t.Canceled)),
            fromStore.ToDictionary(t => (t.ProductId, t.OriginStoreId), t => t.Sent));
    }

    /// <summary>Outro produto da mesma marca, com a mesma referência (3+ caracteres), com estoque positivo na mesma loja.</summary>
    private async Task<Func<StockEntry, string?>> DuplicateFinderAsync(
        IReadOnlyList<StockEntry> entries, IReadOnlyList<StockEntry> negatives, CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => p.Reference != null && p.Reference.Length >= 3)
            .Select(p => new { p.Id, p.Code, p.BrandId, Reference = p.Reference!.ToUpper() })
            .ToListAsync(cancellationToken);
        var byId = products.ToDictionary(p => p.Id);
        var byKey = products.GroupBy(p => (p.BrandId, p.Reference)).Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());
        var positive = entries.Where(e => e.Stock > 0).Select(e => (e.ProductId, e.StoreId)).ToHashSet();

        return e =>
        {
            if (!byId.TryGetValue(e.ProductId, out var product) || !byKey.TryGetValue((product.BrandId, product.Reference), out var siblings))
                return null;
            var sibling = siblings.FirstOrDefault(id => id != e.ProductId && positive.Contains((id, e.StoreId)));
            return sibling == 0 ? null : byId[sibling].Code;
        };
    }

    private sealed record TransferTotals(
        Dictionary<(int ProductId, int StoreId), (decimal Sent, decimal Received, decimal Canceled)> ToStore,
        Dictionary<(int ProductId, int StoreId), decimal> FromStore);
}
