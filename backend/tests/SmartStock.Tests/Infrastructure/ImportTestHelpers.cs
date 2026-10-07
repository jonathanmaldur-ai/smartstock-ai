using System.Net.Http.Headers;
using System.Text;
using ClosedXML.Excel;

namespace SmartStock.Tests.Infrastructure;

/// <summary>Monta planilhas de exemplo (dados inventados) e envia para a API.</summary>
public static class ImportTestHelpers
{
    public static readonly string[] BrandHeaders = ["Codigo da Marca", "Marca", "Status"];

    public static readonly string[] ProductHeaders =
        ["Codigo do Produto", "Descrição do Produto", "promar", "prouni", "Situação", "Categoria", "Categoria 2", "Referencia", "clf_des"];

    public static byte[] Xlsx(string[] headers, params string?[][] rows) => XlsxSheet("Planilha1", headers, rows);

    /// <summary>Planilha com nome de aba (vendas: a aba identifica a loja, ex.: "04 JAGUARIUNA").</summary>
    public static byte[] XlsxSheet(string sheetName, string[] headers, params string?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);
        for (var c = 0; c < headers.Length; c++)
            sheet.Cell(1, c + 1).Value = headers[c];

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                // Tudo como texto, como o ERP exporta (preserva zeros à esquerda).
                sheet.Cell(r + 2, c + 1).Value = rows[r][c] ?? string.Empty;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Csv(string[] headers, IEnumerable<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', headers));
        foreach (var row in rows)
            builder.AppendLine(string.Join(';', row));
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static async Task<HttpResponseMessage> UploadAsync(
        this HttpClient client, string type, string fileName, byte[] content, DateOnly? referenceDate = null, string? storeCode = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (referenceDate is not null)
            form.Add(new StringContent(referenceDate.Value.ToString("yyyy-MM-dd")), "referenceDate");
        if (storeCode is not null)
            form.Add(new StringContent(storeCode), "storeCode");
        return await client.PostAsync($"/api/imports/{type}", form);
    }

    /// <summary>Envia e confirma; falha o teste se algo der errado.</summary>
    public static async Task<ImportReportDto> ImportAndConfirmAsync(
        this HttpClient client, string type, string fileName, byte[] content, DateOnly? referenceDate = null, string? storeCode = null)
    {
        var upload = await client.UploadAsync(type, fileName, content, referenceDate, storeCode);
        upload.EnsureSuccessStatusCode();
        var report = await upload.ReadAsync<ImportReportDto>();
        var confirm = await client.PostAsync($"/api/imports/{report.Id}/confirm", null);
        confirm.EnsureSuccessStatusCode();
        return await confirm.ReadAsync<ImportReportDto>();
    }

    public static string UniqueCode(string prefix) => prefix + Random.Shared.Next(100_000, 999_999);
}

public sealed record ImportReportDto(
    Guid Id,
    string Type,
    string Status,
    string FileName,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    int WarningCount,
    string? RejectionReason,
    Guid? DuplicateOfBatchId,
    DateOnly? ReferenceDate,
    string? StoreCode,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int ReplacedRecords,
    List<IssueGroupDto> IssueGroups);

public sealed record IssueGroupDto(string Severity, string Code, string Message, int Count, List<string> SampleValues);

public sealed record PagedDto<T>(List<T> Items, int Page, int PageSize, int TotalCount);

public sealed record StoreItem(int Id, string Code, string Name, string? City, string Type, string Status);

public sealed record BrandItem(int Id, string Code, string Name, bool IsActive, int ProductCount);

public sealed record CategoryItem(int Id, string Name, bool ExcludedFromAnalysis, int ProductCount, List<string> Aliases);

public sealed record ProductItem(
    int Id, string Code, string Description, string? Unit, string? Reference, bool IsActive,
    string BrandCode, string BrandName, string CategoryName, bool CategoryExcluded, string? SubcategoryName);
