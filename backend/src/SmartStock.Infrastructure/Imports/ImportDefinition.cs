using SmartStock.Domain.Imports;

namespace SmartStock.Infrastructure.Imports;

/// <summary>
/// Regras de um tipo de arquivo: layout aceito, validação linha a linha e gravação das linhas válidas.
/// Novos tipos entram implementando esta interface e sendo registrados em DependencyInjection.
/// </summary>
internal interface IImportDefinition
{
    ImportType Type { get; }

    /// <summary>Estoque e vendas: o arquivo do ERP não traz a data, que é informada pelo usuário.</summary>
    bool RequiresReferenceDate => false;

    /// <summary>Motivo da recusa quando o arquivo não tem o layout esperado; null quando o layout é aceito.</summary>
    string? CheckLayout(SheetData sheet);

    Task<ImportValidation> ValidateAsync(ImportContext context, SheetData sheet, CancellationToken cancellationToken);
}

/// <summary>Dados informados no envio (ou gravados no lote, na revalidação da confirmação).</summary>
internal sealed record ImportContext(string FileName, DateOnly? ReferenceDate, string? StoreCode);

/// <summary>Data, loja e período a que os dados se referem, gravados no lote.</summary>
internal sealed record ImportScope(DateOnly? ReferenceDate = null, int? StoreId = null, DateOnly? PeriodStart = null, DateOnly? PeriodEnd = null)
{
    public static readonly ImportScope None = new();
}

/// <param name="Apply">Grava as linhas válidas. Chamado só na confirmação, dentro de uma transação.</param>
/// <param name="RejectionReason">Recusa do arquivo inteiro descoberta na validação (ex.: loja não identificada).</param>
internal sealed record ImportValidation(
    IssueCollector Issues,
    int ValidRows,
    Func<Guid, CancellationToken, Task<ApplyResult>> Apply,
    ImportScope? Scope = null,
    string? RejectionReason = null)
{
    public static ImportValidation Rejected(string reason) =>
        new(new IssueCollector(), 0, (_, _) => Task.FromResult(ApplyResult.None), RejectionReason: reason);
}

/// <param name="ReplacedRecords">Registros de importações anteriores substituídos por esta.</param>
internal sealed record ApplyResult(int ReplacedRecords)
{
    public static readonly ApplyResult None = new(0);
}

internal static class ImportLayout
{
    /// <summary>Recusa padrão: lista as colunas obrigatórias que não estão no cabeçalho.</summary>
    public static string? RequireColumns(SheetData sheet, params string[] columns)
    {
        var missing = columns.Where(c => !sheet.HasColumn(c)).ToList();
        return missing.Count == 0
            ? null
            : $"O arquivo não parece ser do tipo esperado. Colunas não encontradas: {string.Join(", ", missing)}.";
    }
}

internal sealed class IssueCollector
{
    private readonly List<ImportIssue> _issues = [];
    private readonly HashSet<int> _rowsWithError = [];

    public IReadOnlyList<ImportIssue> All => _issues;
    public int ErrorRowCount => _rowsWithError.Count;
    public int WarningCount => _issues.Count(i => i.Severity == ImportIssueSeverity.Warning);

    public bool HasError(int lineNumber) => _rowsWithError.Contains(lineNumber);

    public void Error(int lineNumber, string? column, string code, string message, string? value = null)
    {
        _rowsWithError.Add(lineNumber);
        Add(lineNumber, column, ImportIssueSeverity.Error, code, message, value);
    }

    public void Warning(int lineNumber, string? column, string code, string message, string? value = null) =>
        Add(lineNumber, column, ImportIssueSeverity.Warning, code, message, value);

    private void Add(int lineNumber, string? column, ImportIssueSeverity severity, string code, string message, string? value) =>
        _issues.Add(new ImportIssue
        {
            RowNumber = lineNumber,
            Column = column,
            Severity = severity,
            Code = code,
            Message = message,
            Value = value is { Length: > 300 } ? value[..300] : value
        });
}
