using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

/// <summary>
/// Decisão 9: a sugestão aprovada só vira "Realizada" quando a transferência aparece no arquivo importado.
/// Critério: saída (não cancelada) do mesmo produto, da mesma origem para o mesmo destino, no dia da aprovação ou depois.
/// </summary>
internal sealed class CompletedTransferMatcher(SmartStockDbContext db)
{
    /// <returns>Quantas sugestões aprovadas passaram a "Realizada".</returns>
    public async Task<int> MarkCompletedAsync(CancellationToken cancellationToken)
    {
        var approved = await db.TransferSuggestions
            .Where(s => s.Status == SuggestionStatus.Approved && s.DecidedAt != null)
            .ToListAsync(cancellationToken);
        if (approved.Count == 0)
            return 0;

        var productIds = approved.Select(s => s.ProductId).Distinct().ToList();
        var earliest = approved.Min(s => DecisionDay(s));
        var shipments = await db.TransferMovements.AsNoTracking()
            .Where(m => productIds.Contains(m.ProductId) && m.Direction == TransferDirection.Out && !m.IsCancellation && m.Date >= earliest)
            .Select(m => new { m.ProductId, m.OriginStoreId, m.DestinationStoreId, m.Date, m.Quantity })
            .ToListAsync(cancellationToken);
        var byRoute = shipments.ToLookup(m => (m.ProductId, m.OriginStoreId, m.DestinationStoreId));

        var completed = 0;
        foreach (var suggestion in approved)
        {
            var decisionDay = DecisionDay(suggestion);
            var sent = byRoute[(suggestion.ProductId, suggestion.OriginStoreId, suggestion.DestinationStoreId)]
                .Where(m => m.Date >= decisionDay)
                .ToList();
            if (sent.Count == 0)
                continue;

            suggestion.Status = SuggestionStatus.Completed;
            suggestion.CompletedOn = sent.Min(m => m.Date);
            suggestion.CompletedQuantity = sent.Sum(m => m.Quantity);
            completed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return completed;
    }

    /// <summary>Dia da aprovação no horário local (o arquivo de transferências tem só a data).</summary>
    private static DateOnly DecisionDay(TransferSuggestion suggestion) =>
        DateOnly.FromDateTime(suggestion.DecidedAt!.Value.ToLocalTime().DateTime);
}
