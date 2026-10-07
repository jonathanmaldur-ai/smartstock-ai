using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Catalog;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Seeding;

/// <summary>
/// Cria o que estiver faltando da carga inicial. Não altera o que já existe:
/// mudanças feitas pelo Administrador (nomes, exclusões, unificações) são preservadas.
/// </summary>
internal sealed class CatalogSeeder(SmartStockDbContext db)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingCodes = await db.Stores.Select(s => s.Code).ToListAsync(cancellationToken);
        db.Stores.AddRange(CatalogSeedData.Stores
            .Where(s => !existingCodes.Contains(s.Code))
            .Select(s => new Store(s.Code, s.Name, s.City, s.Type, s.Status)));

        var categories = await db.Categories.Include(c => c.Aliases).ToListAsync(cancellationToken);
        var knownKeys = categories
            .SelectMany(c => c.Aliases.Select(a => a.Alias).Append(CatalogText.Key(c.Name)))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in CatalogSeedData.CategoryAliases.Keys.Union(CatalogSeedData.ExcludedCategories))
        {
            if (knownKeys.Contains(CatalogText.Key(name)))
                continue;

            var category = new Category { Name = name, ExcludedFromAnalysis = CatalogSeedData.ExcludedCategories.Contains(name) };
            db.Categories.Add(category);
            categories.Add(category);
            knownKeys.Add(CatalogText.Key(name));
        }

        foreach (var (name, aliases) in CatalogSeedData.CategoryAliases)
        {
            var category = categories.Single(c => CatalogText.Key(c.Name) == CatalogText.Key(name)
                                                  || c.Aliases.Any(a => a.Alias == CatalogText.Key(name)));
            foreach (var alias in aliases.Select(CatalogText.Key).Where(knownKeys.Add))
                category.Aliases.Add(new CategoryAlias { Alias = alias });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
