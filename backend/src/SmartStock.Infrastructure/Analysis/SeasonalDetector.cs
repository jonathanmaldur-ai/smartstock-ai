using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Catalog;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

/// <summary>
/// Gera as sugestões de sazonalidade a cada análise (decisão 65): palavra-chave da época na categoria, subcategoria ou
/// descrição, ou venda que parou (vendia bem em 12 meses e quase nada em 60 dias). Só sugere: quem confirma é o
/// Administrador. Sugestão rejeitada não volta para a mesma época; produto já sazonal não recebe sugestão.
/// </summary>
internal sealed class SeasonalDetector(SmartStockDbContext db, RecentSalesLoader recentSales, TimeProvider clock)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Venda de 12 meses de um produto numa loja, como saiu na análise.</summary>
    public sealed record ProductSales(int ProductId, int StoreId, decimal Sold12Months);

    /// <returns>Quantas sugestões novas foram criadas.</returns>
    public async Task<int> DetectAsync(DateOnly analysisDate, IReadOnlyList<ProductSales> sales, CancellationToken cancellationToken)
    {
        var seasons = await db.Seasons.AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken);
        var window = await recentSales.LoadAsync(SeasonalRules.StoppedWindowDays, analysisDate, cancellationToken);
        var analysed = sales.Select(s => s.ProductId).ToHashSet();
        var products = (await db.Products.AsNoTracking()
                .Where(p => p.SeasonId == null && !p.IsSeasonal)
                .Select(p => new { p.Id, p.Description, Category = p.Category.Name, Subcategory = p.Subcategory != null ? p.Subcategory.Name : null })
                .ToListAsync(cancellationToken))
            .Where(p => analysed.Contains(p.Id))
            .ToList();
        var soldByProduct = window is null
            ? new Dictionary<int, decimal>()
            : sales.Where(s => window.CoveredStores.Contains(s.StoreId))
                .GroupBy(s => s.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.Sold12Months));
        var existing = (await db.SeasonalSuggestions.ToListAsync(cancellationToken)).ToLookup(s => s.ProductId);

        var now = clock.GetUtcNow();
        var created = 0;
        foreach (var product in products)
        {
            var match = SeasonalRules.MatchKeyword(seasons, product.Category, product.Subcategory, product.Description);
            var sold12 = soldByProduct.GetValueOrDefault(product.Id);
            var recent = window is null ? 0 : window.CoveredStores.Sum(store => window.SoldIn(product.Id, store));
            var stopped = window is not null && SeasonalRules.HasStopped(sold12, recent);
            if (match is null && !stopped)
                continue;

            var kind = stopped ? SeasonalSuggestionKind.SalesStopped : SeasonalSuggestionKind.Keyword;
            var seasonId = match?.Season.Id;
            var evidence = stopped
                ? $"Vendeu {sold12.ToString("N0", PtBr)} un. em 12 meses e {recent.ToString("N0", PtBr)} nos últimos {SeasonalRules.StoppedWindowDays} dias"
                  + (match is null ? string.Empty : $" · {match.Value.Evidence}")
                : match!.Value.Evidence;

            var previous = existing[product.Id].ToList();
            var pending = previous.FirstOrDefault(s => s.Status == SeasonalSuggestionStatus.Pending);
            if (pending is not null)
            {
                if (pending.Kind != kind || pending.SeasonId != seasonId || pending.Evidence != evidence)
                {
                    (pending.Kind, pending.SeasonId, pending.Evidence, pending.UpdatedAt) = (kind, seasonId, evidence, now);
                }
                continue;
            }
            if (previous.Any(s => s.SeasonId == seasonId && (s.Kind == kind || s.Status == SeasonalSuggestionStatus.Confirmed)))
                continue;

            db.SeasonalSuggestions.Add(new SeasonalSuggestion
            {
                ProductId = product.Id, SeasonId = seasonId, Kind = kind, Evidence = evidence, CreatedAt = now, UpdatedAt = now
            });
            created++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return created;
    }
}
