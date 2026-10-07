using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports.Definitions;

/// <summary>Arquivo de marcas do ERP (Marca.xlsx): Codigo da Marca | Marca | Status.</summary>
internal sealed class BrandImportDefinition(SmartStockDbContext db, TimeProvider clock) : IImportDefinition
{
    private const string CodeColumn = "Codigo da Marca";
    private const string NameColumn = "Marca";
    private const string StatusColumn = "Status";

    public ImportType Type => ImportType.Brands;
    public string? CheckLayout(SheetData sheet) => ImportLayout.RequireColumns(sheet, CodeColumn, NameColumn);

    public Task<ImportValidation> ValidateAsync(ImportContext context, SheetData sheet, CancellationToken cancellationToken)
    {
        var issues = new IssueCollector();
        var records = new Dictionary<string, BrandRecord>(StringComparer.Ordinal);

        foreach (var row in sheet.Rows)
        {
            var code = CodeNormalizer.Brand(row.Get(CodeColumn));
            var name = row.Get(NameColumn);

            if (code is null)
                issues.Error(row.LineNumber, CodeColumn, "brand.code_missing", "Código da marca não informado.");
            else if (code.Length > 20)
                issues.Error(row.LineNumber, CodeColumn, "brand.code_too_long", "Código da marca com mais de 20 caracteres.", code);
            else if (records.ContainsKey(code))
                issues.Error(row.LineNumber, CodeColumn, "brand.duplicated", "Marca repetida no arquivo. Vale a primeira ocorrência.", code);

            if (name is null)
                issues.Error(row.LineNumber, NameColumn, "brand.name_missing", "Nome da marca não informado.");

            if (issues.HasError(row.LineNumber))
                continue;

            records[code!] = new BrandRecord(code!, Truncate(name!, 150), ParseActive(row, issues));
        }

        return Task.FromResult(new ImportValidation(issues, records.Count, (batchId, ct) => ApplyAsync(records.Values, batchId, ct)));
    }

    private async Task<ApplyResult> ApplyAsync(IEnumerable<BrandRecord> records, Guid batchId, CancellationToken cancellationToken)
    {
        var existing = await db.Brands.ToDictionaryAsync(b => b.Code, cancellationToken);
        var now = clock.GetUtcNow();

        foreach (var record in records)
        {
            if (!existing.TryGetValue(record.Code, out var brand))
            {
                brand = new Brand { Code = record.Code };
                db.Brands.Add(brand);
            }
            brand.Name = record.Name;
            brand.IsActive = record.IsActive;
            brand.UpdatedAt = now;
            brand.LastImportId = batchId;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ApplyResult.None;
    }

    private static bool ParseActive(SheetRow row, IssueCollector issues)
    {
        var status = row.Get(StatusColumn);
        if (status is null or "1")
            return true;
        if (status == "0")
            return false;

        issues.Warning(row.LineNumber, StatusColumn, "brand.status_unknown", "Status desconhecido; a marca foi considerada ativa.", status);
        return true;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record BrandRecord(string Code, string Name, bool IsActive);
}
