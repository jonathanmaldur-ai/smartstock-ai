using SmartStock.Application.Common;
using SmartStock.Domain.Imports;

namespace SmartStock.Application.Imports;

/// <summary>
/// Fluxo de importação (decisão 21): enviar → validar → relatório → o usuário confirma ou descarta.
/// Só a confirmação grava nos cadastros.
/// </summary>
public interface IImportService
{
    Task<Result<ImportReport>> UploadAsync(
        ImportType type, Stream content, string fileName, ImportParameters parameters, CancellationToken cancellationToken = default);
    /// <summary>
    /// Tipo da planilha pelo conteúdo (colunas), sem depender do nome do arquivo: cada importação diz se o layout é dela.
    /// null quando nenhum tipo reconhece o arquivo.
    /// </summary>
    ImportType? DetectType(string filePath);

    Task<Result<ImportReport>> ConfirmAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<Result<ImportReport>> DiscardAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<Result<ImportReport>> GetAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<PagedResult<ImportReport>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<ImportIssueDto>>> GetIssuesAsync(Guid batchId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Todas as ocorrências do lote, para baixar em CSV.</summary>
    IAsyncEnumerable<ImportIssueDto> StreamIssuesAsync(Guid batchId, CancellationToken cancellationToken = default);
}

public sealed record ImportReport(
    Guid Id,
    ImportType Type,
    ImportStatus Status,
    string FileName,
    DateTimeOffset UploadedAt,
    string UploadedByEmail,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    int WarningCount,
    string? RejectionReason,
    Guid? DuplicateOfBatchId,
    DateTimeOffset? DecidedAt,
    string? DecidedByEmail,
    DateOnly? ReferenceDate,
    string? StoreCode,
    string? StoreName,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    int ReplacedRecords,
    IReadOnlyList<ImportIssueGroup> IssueGroups);

/// <summary>Dados informados no envio: data dos dados (estoque e vendas) e loja (vendas; sem ela, vem do nome da aba).</summary>
public sealed record ImportParameters(DateOnly? ReferenceDate = null, string? StoreCode = null)
{
    public static readonly ImportParameters None = new();
}

/// <summary>Ocorrências agrupadas por tipo, com alguns valores de exemplo, para o resumo do relatório.</summary>
public sealed record ImportIssueGroup(
    ImportIssueSeverity Severity, string Code, string Message, int Count, IReadOnlyList<string> SampleValues);

public sealed record ImportIssueDto(int RowNumber, string? Column, ImportIssueSeverity Severity, string Code, string Message, string? Value);
