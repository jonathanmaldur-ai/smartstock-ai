using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports.Definitions;

/// <summary>
/// Arquivo de estoque do ERP ("quantidade por empresa"): uma linha por produto e uma coluna por loja.
/// Aceita o arquivo como sai do ERP (colunas "codigoproduto", "qtde", "qtd01"...) e a versão tratada à mão
/// ("Codigo do Produto", "Quantidade Geral", "01 MATRIZ"...); as demais colunas são ignoradas.
/// Cada importação é uma foto do estoque na data informada (decisão 31). Grava só as quantidades diferentes de zero.
/// </summary>
internal sealed partial class StockImportDefinition(SmartStockDbContext db, TimeProvider clock) : IImportDefinition
{
    /// <summary>Colunas do arquivo bruto com os dados do produto: bastam para cadastrar produto novo (decisão 46).</summary>
    private const string RawDescriptionColumn = "descricao";
    private const string RawBrandColumn = "marca";
    private const string RawUnitColumn = "unidade";
    private const string RawCategoryColumn = "setor";
    private const string RawSubcategoryColumn = "linha";
    private const string RawReferenceColumn = "referencia";

    private static readonly string[] CodeColumns = ["Codigo do Produto", "codigoproduto"];
    private static readonly string[] TotalColumns = ["Quantidade Geral", "qtde"];

    /// <summary>Linha final de totais do relatório.</summary>
    private const string TotalsRowMarker = "TOTAIS";

    [GeneratedRegex(@"^QTD(\d{1,3})$")]
    private static partial Regex RawStoreColumn();

    public ImportType Type => ImportType.Stock;
    public bool RequiresReferenceDate => true;

    public string? CheckLayout(SheetData sheet)
    {
        if (!CodeColumns.Any(sheet.HasColumn))
            return "O arquivo não parece ser do tipo esperado. Coluna não encontrada: código do produto (\"Codigo do Produto\" ou \"codigoproduto\").";
        return sheet.Headers.Any(h => StoreCodeOf(h) is not null)
            ? null
            : "O arquivo não tem colunas de loja (ex.: \"01 MATRIZ\" ou \"qtd01\"). Confira se é o relatório de estoque por empresa.";
    }

    /// <summary>"01 MATRIZ" (tratado) ou "QTD01" (bruto do ERP) viram "01"; outras colunas, null.</summary>
    private static string? StoreCodeOf(string header) =>
        Store.NormalizeCode(header) ?? (RawStoreColumn().Match(header) is { Success: true } m ? Store.NormalizeCode(m.Groups[1].Value) : null);

    public async Task<ImportValidation> ValidateAsync(ImportContext context, SheetData sheet, CancellationToken cancellationToken)
    {
        var codeColumn = CodeColumns.First(sheet.HasColumn);
        var totalColumn = TotalColumns.FirstOrDefault(sheet.HasColumn);
        var issues = new IssueCollector();
        var stores = await StoreLookup.LoadAsync(db, cancellationToken);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, cancellationToken);
        var catalog = sheet.HasColumn(RawDescriptionColumn) && sheet.HasColumn(RawBrandColumn)
            ? await ProductCatalogBuilder.LoadAsync(db, clock, cancellationToken)
            : null;

        var storeColumns = ResolveStoreColumns(sheet, stores, issues);
        var allStoreColumns = sheet.Headers.Where(h => StoreCodeOf(h) is not null).ToList();
        var levels = new List<StockRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var validProducts = 0;

        foreach (var row in sheet.Rows)
        {
            var code = row.Get(codeColumn);
            if (code == TotalsRowMarker)
                continue;

            var known = ValidateProduct(row, codeColumn, code, products, catalog, seen, issues);
            var quantities = ReadQuantities(row, allStoreColumns, issues);
            if (issues.HasError(row.LineNumber))
                continue;

            validProducts++;
            if (totalColumn is not null)
                CheckTotal(row, totalColumn, quantities.Values.Sum(), issues);
            foreach (var (column, storeId) in storeColumns)
            {
                if (quantities[column] != 0)
                    levels.Add(new StockRecord(code!, storeId, quantities[column]));
            }
        }

        return new ImportValidation(
            issues,
            validProducts,
            (batchId, ct) => ApplyAsync(levels, products, catalog, batchId, ct),
            new ImportScope(ReferenceDate: context.ReferenceDate));
    }

    /// <summary>Colunas de loja ativas. Loja fechada ou não cadastrada: coluna ignorada, com aviso (decisão 4).</summary>
    private static List<(string Column, int StoreId)> ResolveStoreColumns(SheetData sheet, StoreLookup stores, IssueCollector issues)
    {
        var columns = new List<(string, int)>();
        foreach (var header in sheet.Headers.Where(h => StoreCodeOf(h) is not null))
        {
            var store = stores.Find(StoreCodeOf(header));
            if (store is null)
                issues.Warning(1, header, "stock.store_unknown", "Loja não cadastrada: coluna ignorada.", header);
            else if (!store.IsActive)
                issues.Warning(1, header, "stock.store_closed", "Loja fechada: coluna ignorada (decisão 4).", header);
            else
                columns.Add((header, store.Id));
        }
        return columns;
    }

    private static bool ValidateProduct(
        SheetRow row, string codeColumn, string? code, Dictionary<string, int> products, ProductCatalogBuilder? catalog,
        HashSet<string> seen, IssueCollector issues)
    {
        if (code is null)
        {
            issues.Error(row.LineNumber, codeColumn, "stock.code_missing", "Código do produto não informado.");
            return false;
        }
        if (!seen.Add(code))
        {
            issues.Error(row.LineNumber, codeColumn, "stock.duplicated", "Produto repetido no arquivo. Vale a primeira ocorrência.", code);
            return false;
        }
        if (products.ContainsKey(code) || TryAddNewProduct(row, code, catalog, issues))
            return true;

        issues.Error(row.LineNumber, codeColumn, "stock.product_not_found",
            "Produto não cadastrado: importe antes o arquivo de produtos atualizado.", code);
        return false;
    }

    /// <summary>
    /// Decisão 46: produto que ainda não está no cadastro é criado com os dados do próprio arquivo bruto de estoque
    /// (conferidos com o cadastro: descrição, marca, unidade, setor e linha iguais em 100% dos produtos).
    /// </summary>
    private static bool TryAddNewProduct(SheetRow row, string code, ProductCatalogBuilder? catalog, IssueCollector issues)
    {
        var description = row.Get(RawDescriptionColumn);
        var brandCode = CodeNormalizer.Brand(row.Get(RawBrandColumn));
        if (catalog is null || description is null || brandCode is null || code.Length > 30)
            return false;

        catalog.Add(
            new ProductInput(code, description, brandCode, row.Get(RawUnitColumn), row.Get(RawReferenceColumn), null, true,
                row.Get(RawCategoryColumn), row.Get(RawSubcategoryColumn)),
            (RawBrandColumn, RawCategoryColumn),
            row.LineNumber,
            issues);
        issues.Warning(row.LineNumber, RawDescriptionColumn, "stock.product_created",
            "Produto novo no ERP: cadastrado com os dados do arquivo de estoque.", $"{code} {description}");
        return true;
    }

    private static Dictionary<string, decimal> ReadQuantities(SheetRow row, List<string> storeColumns, IssueCollector issues)
    {
        var quantities = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var column in storeColumns)
        {
            var raw = row.Get(column);
            var quantity = NumberParser.Parse(raw);
            if (quantity is null)
                issues.Error(row.LineNumber, column, "stock.invalid_quantity", "Quantidade inválida.", raw);
            quantities[column] = quantity ?? 0;
        }
        return quantities;
    }

    private static void CheckTotal(SheetRow row, string totalColumn, decimal sumOfStores, IssueCollector issues)
    {
        var total = NumberParser.Parse(row.Get(totalColumn));
        if (total is not null && row.Get(totalColumn) is not null && Math.Abs(total.Value - sumOfStores) > 0.001m)
            issues.Warning(row.LineNumber, totalColumn, "stock.total_mismatch",
                "A \"Quantidade Geral\" não bate com a soma das lojas; foram usadas as quantidades de cada loja.",
                $"{total.Value:0.####} ≠ {sumOfStores:0.####}");
    }

    private async Task<ApplyResult> ApplyAsync(
        List<StockRecord> levels, Dictionary<string, int> products, ProductCatalogBuilder? catalog, Guid batchId, CancellationToken cancellationToken)
    {
        if (catalog is not null)
            foreach (var (code, id) in await catalog.SaveAsync(batchId, cancellationToken))
                products[code] = id;

        await PostgresCopy.WriteAsync(db,
            "COPY stock_levels (import_id, product_id, store_id, quantity) FROM STDIN (FORMAT BINARY)",
            levels,
            async (writer, level, ct) =>
            {
                await writer.WriteAsync(batchId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(products[level.ProductCode], NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(level.StoreId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(level.Quantity, NpgsqlDbType.Numeric, ct);
            },
            cancellationToken);
        return ApplyResult.None;
    }

    private sealed record StockRecord(string ProductCode, int StoreId, decimal Quantity);
}
