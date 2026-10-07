using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Catalog;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports;

/// <summary>Resolve o texto da planilha para a categoria oficial, pelo nome ou pela tabela de correção (de-para).</summary>
internal sealed class CategoryLookup
{
    private readonly Dictionary<string, string> _byKey;

    private CategoryLookup(Dictionary<string, string> byKey) => _byKey = byKey;

    public static async Task<CategoryLookup> LoadAsync(SmartStockDbContext db, CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking()
            .Select(c => new { c.Name, Aliases = c.Aliases.Select(a => a.Alias).ToList() })
            .ToListAsync(cancellationToken);

        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var category in categories)
        {
            byKey.TryAdd(CatalogText.Key(category.Name), category.Name);
            foreach (var alias in category.Aliases)
                byKey[alias] = category.Name;
        }
        return new CategoryLookup(byKey);
    }

    public bool TryResolve(string key, out string categoryName) => _byKey.TryGetValue(key, out categoryName!);
}
