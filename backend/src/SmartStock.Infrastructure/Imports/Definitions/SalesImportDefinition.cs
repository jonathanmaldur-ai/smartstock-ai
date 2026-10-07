using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports.Definitions;

/// <summary>
/// Vendas por loja do ERP: um arquivo por loja com o acumulado de 12 meses até a data do relatório (decisões 1 e 32).
/// Dois layouts oficiais (decisão 7): o da MATRIZ ("Codigo Produto", "Qtd Vendida" como texto, "valor vendido")
/// e o das demais lojas ("ivpro", "qtde", "valor"). A loja vem da escolha na tela ou do nome da aba/arquivo.
/// </summary>
internal sealed class SalesImportDefinition(SmartStockDbContext db) : IImportDefinition
{
    private static readonly SalesLayout MatrizLayout = new("Codigo Produto", "Qtd Vendida", "valor vendido");
    private static readonly SalesLayout StoreLayout = new("ivpro", "qtde", "valor");

    public ImportType Type => ImportType.Sales;
    public bool RequiresReferenceDate => true;

    public string? CheckLayout(SheetData sheet) =>
        FindLayout(sheet) is null
            ? "O arquivo não parece ser de vendas por loja. Esperado: colunas \"ivpro\" e \"qtde\" " +
              "(ou \"Codigo Produto\" e \"Qtd Vendida\", no arquivo da MATRIZ)."
            : null;

    public async Task<ImportValidation> ValidateAsync(ImportContext context, SheetData sheet, CancellationToken cancellationToken)
    {
        var stores = await StoreLookup.LoadAsync(db, cancellationToken);
        var store = context.StoreCode is not null
            ? stores.Find(context.StoreCode)
            : stores.Find(sheet.SheetName) ?? stores.Find(Path.GetFileNameWithoutExtension(context.FileName))
              ?? stores.FindByName(Path.GetFileNameWithoutExtension(context.FileName)) ?? stores.FindByName(sheet.SheetName);
        if (store is null && context.StoreCode is not null)
            return ImportValidation.Rejected($"Loja {context.StoreCode} não cadastrada.");
        if (store is null)
            return ImportValidation.Rejected(
                "Não foi possível identificar a loja pelo nome da aba nem do arquivo (ex.: \"04 JAGUARIUNA\" ou \"JAGUARIUNA\"). Escolha a loja no envio.");
        if (!store.IsActive)
            return ImportValidation.Rejected($"A loja {store.Code} {store.Name} está fechada: vendas não são importadas (decisão 4).");

        var issues = new IssueCollector();
        WarnIfSheetPointsToAnotherStore(context, sheet, stores, store, issues);

        var layout = FindLayout(sheet)!;
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, cancellationToken);
        var records = new Dictionary<int, SalesRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in sheet.Rows)
        {
            var record = ValidateRow(row, layout, products, seen, issues);
            if (record is not null && record.Quantity != 0)
                records[record.ProductId] = record;
        }

        var date = context.ReferenceDate!.Value;
        return new ImportValidation(
            issues,
            records.Count,
            (batchId, ct) => ApplyAsync(records.Values, store.Id, batchId, ct),
            new ImportScope(date, store.Id, PeriodStart: date.AddYears(-1), PeriodEnd: date));
    }

    private static SalesLayout? FindLayout(SheetData sheet) =>
        new[] { StoreLayout, MatrizLayout }.FirstOrDefault(l => sheet.HasColumn(l.Code) && sheet.HasColumn(l.Quantity));

    /// <summary>Conferência: a loja escolhida na tela diferente da indicada pela aba merece atenção.</summary>
    private static void WarnIfSheetPointsToAnotherStore(
        ImportContext context, SheetData sheet, StoreLookup stores, Store chosen, IssueCollector issues)
    {
        var fromSheet = stores.Find(sheet.SheetName);
        if (context.StoreCode is not null && fromSheet is not null && fromSheet.Id != chosen.Id)
            issues.Warning(1, null, "sales.store_mismatch",
                $"A aba da planilha indica a loja {fromSheet.Code} {fromSheet.Name}, mas foi escolhida a {chosen.Code} {chosen.Name}.",
                sheet.SheetName);
    }

    private static SalesRecord? ValidateRow(
        SheetRow row, SalesLayout layout, Dictionary<string, int> products, HashSet<string> seen, IssueCollector issues)
    {
        var code = row.Get(layout.Code);
        if (code is null)
        {
            issues.Error(row.LineNumber, layout.Code, "sales.code_missing", "Código do produto não informado.");
            return null;
        }
        if (!seen.Add(code))
        {
            issues.Error(row.LineNumber, layout.Code, "sales.duplicated", "Produto repetido no arquivo. Vale a primeira ocorrência.", code);
            return null;
        }
        if (!products.TryGetValue(code, out var productId))
        {
            issues.Error(row.LineNumber, layout.Code, "sales.product_not_found",
                "Produto não cadastrado: importe antes o arquivo de produtos atualizado.", code);
            return null;
        }

        var rawQuantity = row.Get(layout.Quantity);
        var quantity = NumberParser.Parse(rawQuantity);
        if (quantity is null)
        {
            issues.Error(row.LineNumber, layout.Quantity, "sales.invalid_quantity", "Quantidade vendida inválida.", rawQuantity);
            return null;
        }
        if (quantity < 0)
            issues.Warning(row.LineNumber, layout.Quantity, "sales.negative_quantity",
                "Venda negativa (mais devoluções que vendas no período).", rawQuantity);

        var amount = NumberParser.Parse(row.Get(layout.Amount));
        return new SalesRecord(productId, quantity.Value, amount);
    }

    private async Task<ApplyResult> ApplyAsync(IEnumerable<SalesRecord> records, int storeId, Guid batchId, CancellationToken cancellationToken)
    {
        await PostgresCopy.WriteAsync(db,
            "COPY sales_totals (import_id, product_id, store_id, quantity, amount) FROM STDIN (FORMAT BINARY)",
            records,
            async (writer, record, ct) =>
            {
                await writer.WriteAsync(batchId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(record.ProductId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(storeId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(record.Quantity, NpgsqlDbType.Numeric, ct);
                if (record.Amount is null)
                    await writer.WriteNullAsync(ct);
                else
                    await writer.WriteAsync(record.Amount.Value, NpgsqlDbType.Numeric, ct);
            },
            cancellationToken);
        return ApplyResult.None;
    }

    private sealed record SalesLayout(string Code, string Quantity, string Amount);

    private sealed record SalesRecord(int ProductId, decimal Quantity, decimal? Amount);
}
