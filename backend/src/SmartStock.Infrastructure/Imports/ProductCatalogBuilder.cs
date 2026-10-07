using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using SmartStock.Domain.Catalog;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports;

/// <summary>
/// Regras de cadastro de produtos numa importação, usadas pelo arquivo de produtos e pelo estoque (decisão 46):
/// marca que não existe vira provisória (decisão 29), a categoria passa pela correção (de-para) e marcas provisórias,
/// categorias e subcategorias novas são criadas só na confirmação.
/// </summary>
internal sealed class ProductCatalogBuilder
{
    private readonly SmartStockDbContext _db;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, int> _brands;
    private readonly CategoryLookup _categories;
    private readonly Dictionary<string, string> _subcategories;
    private readonly Dictionary<string, ProductRecord> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _newCategories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _newSubcategories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _newBrands = new(StringComparer.Ordinal);

    private ProductCatalogBuilder(
        SmartStockDbContext db, TimeProvider clock, Dictionary<string, int> brands, CategoryLookup categories, Dictionary<string, string> subcategories)
    {
        _db = db;
        _clock = clock;
        _brands = brands;
        _categories = categories;
        _subcategories = subcategories;
    }

    public static async Task<ProductCatalogBuilder> LoadAsync(SmartStockDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        new(db, clock,
            await db.Brands.AsNoTracking().ToDictionaryAsync(b => b.Code, b => b.Id, cancellationToken),
            await CategoryLookup.LoadAsync(db, cancellationToken),
            await db.Subcategories.AsNoTracking().ToDictionaryAsync(s => CatalogText.Key(s.Name), s => s.Name, cancellationToken));

    public static string PlaceholderBrandName(string code) => $"MARCA {code} (SEM CADASTRO)";

    public int Count => _records.Count;

    public bool Contains(string code) => _records.ContainsKey(code);

    /// <param name="columns">Nomes das colunas de marca e categoria no arquivo, para os avisos.</param>
    public void Add(ProductInput product, (string Brand, string Category) columns, int lineNumber, IssueCollector issues)
    {
        if (!_brands.ContainsKey(product.BrandCode))
        {
            _newBrands.Add(product.BrandCode);
            issues.Warning(lineNumber, columns.Brand, "product.brand_placeholder",
                "Marca não está no arquivo de marcas: criada como provisória (\"sem cadastro\"). Corrija no ERP e reimporte as marcas.",
                product.BrandCode);
        }

        _records[product.Code] = new ProductRecord(
            product.Code,
            Truncate(product.Description, 300)!,
            Truncate(product.Unit, 10),
            Truncate(product.Reference, 100),
            Truncate(product.FiscalDescription, 300),
            product.IsActive,
            product.BrandCode,
            ResolveCategory(product.Category, columns.Category, lineNumber, issues),
            ResolveSubcategory(product.Subcategory));
    }

    /// <summary>Cria marcas provisórias, categorias e subcategorias novas e grava os produtos. Devolve o id de cada código.</summary>
    public async Task<Dictionary<string, int>> SaveAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (_records.Count == 0)
            return [];

        var now = _clock.GetUtcNow();
        foreach (var code in _newBrands)
            _db.Brands.Add(new Brand { Code = code, Name = PlaceholderBrandName(code), IsActive = true, UpdatedAt = now, LastImportId = batchId });
        foreach (var name in _newCategories.Values)
            _db.Categories.Add(new Category { Name = Truncate(name, 100)! });
        foreach (var name in _newSubcategories.Values)
            _db.Subcategories.Add(new Subcategory { Name = name });
        await _db.SaveChangesAsync(cancellationToken);

        await BulkUpsertAsync(batchId, now, cancellationToken);
        var codes = _records.Keys.ToList();
        return await _db.Products.AsNoTracking().Where(p => codes.Contains(p.Code)).ToDictionaryAsync(p => p.Code, p => p.Id, cancellationToken);
    }

    private string ResolveCategory(string? raw, string column, int lineNumber, IssueCollector issues)
    {
        if (raw is null)
        {
            issues.Warning(lineNumber, column, "product.category_missing", $"Produto sem categoria: ficará em \"{CatalogText.NoCategory}\".");
            raw = CatalogText.NoCategory;
        }

        var key = CatalogText.Key(raw);
        if (_categories.TryResolve(key, out var name))
            return name;

        if (_newCategories.TryAdd(key, raw))
            issues.Warning(lineNumber, column, "product.category_new",
                "Categoria nova: será criada. Revise em Cadastros > Categorias se ela deve ser unificada com outra.", raw);
        return _newCategories[key];
    }

    private string? ResolveSubcategory(string? raw)
    {
        if (raw is null)
            return null;
        var key = CatalogText.Key(raw);
        if (_subcategories.TryGetValue(key, out var name))
            return name;
        _newSubcategories.TryAdd(key, Truncate(raw, 100)!);
        return _newSubcategories[key];
    }

    /// <summary>Carga em lote: COPY para uma tabela temporária e um único INSERT ... ON CONFLICT.</summary>
    private async Task BulkUpsertAsync(Guid batchId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var brandIds = await _db.Brands.AsNoTracking().ToDictionaryAsync(b => b.Code, b => b.Id, cancellationToken);
        var categoryIds = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Name, c => c.Id, cancellationToken);
        var subcategoryIds = await _db.Subcategories.AsNoTracking().ToDictionaryAsync(s => s.Name, s => s.Id, cancellationToken);
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();

        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TEMP TABLE tmp_products (
                code text, description text, unit text, reference text, fiscal_description text,
                is_active boolean, brand_id integer, category_id integer, subcategory_id integer
            ) ON COMMIT DROP
            """, cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY tmp_products (code, description, unit, reference, fiscal_description, is_active, brand_id, category_id, subcategory_id) FROM STDIN (FORMAT BINARY)",
            cancellationToken))
        {
            foreach (var r in _records.Values)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(r.Code, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(r.Description, NpgsqlDbType.Text, cancellationToken);
                await WriteNullableAsync(writer, r.Unit, cancellationToken);
                await WriteNullableAsync(writer, r.Reference, cancellationToken);
                await WriteNullableAsync(writer, r.FiscalDescription, cancellationToken);
                await writer.WriteAsync(r.IsActive, NpgsqlDbType.Boolean, cancellationToken);
                await writer.WriteAsync(brandIds[r.BrandCode], NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(categoryIds[r.CategoryName], NpgsqlDbType.Integer, cancellationToken);
                if (r.SubcategoryName is null)
                    await writer.WriteNullAsync(cancellationToken);
                else
                    await writer.WriteAsync(subcategoryIds[r.SubcategoryName], NpgsqlDbType.Integer, cancellationToken);
            }
            await writer.CompleteAsync(cancellationToken);
        }

        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO products (code, description, unit, reference, fiscal_description, is_active,
                                  brand_id, category_id, subcategory_id, updated_at, last_import_id)
            SELECT code, description, unit, reference, fiscal_description, is_active,
                   brand_id, category_id, subcategory_id, {now}, {batchId}
            FROM tmp_products
            ON CONFLICT (code) DO UPDATE SET
                description = EXCLUDED.description,
                unit = EXCLUDED.unit,
                reference = EXCLUDED.reference,
                fiscal_description = EXCLUDED.fiscal_description,
                is_active = EXCLUDED.is_active,
                brand_id = EXCLUDED.brand_id,
                category_id = EXCLUDED.category_id,
                subcategory_id = EXCLUDED.subcategory_id,
                updated_at = EXCLUDED.updated_at,
                last_import_id = EXCLUDED.last_import_id
            """, cancellationToken);
    }

    private static Task WriteNullableAsync(NpgsqlBinaryImporter writer, string? value, CancellationToken cancellationToken) =>
        value is null ? writer.WriteNullAsync(cancellationToken) : writer.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;

    private sealed record ProductRecord(
        string Code, string Description, string? Unit, string? Reference, string? FiscalDescription,
        bool IsActive, string BrandCode, string CategoryName, string? SubcategoryName);
}

/// <summary>Dados de um produto lidos de uma planilha, já validados (código, descrição e marca presentes).</summary>
internal sealed record ProductInput(
    string Code, string Description, string BrandCode, string? Unit, string? Reference, string? FiscalDescription,
    bool IsActive, string? Category, string? Subcategory);
