using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Common;
using SmartStock.Application.Imports;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Imports;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports;

internal sealed class ImportService(
    SmartStockDbContext db,
    IEnumerable<IImportDefinition> definitions,
    IAuditLogger audit,
    ICurrentUser currentUser,
    IOptions<ImportOptions> options,
    IHostEnvironment environment,
    TimeProvider clock,
    ILogger<ImportService> logger) : IImportService
{
    /// <summary>Limite de ocorrências gravadas por lote; as contagens continuam exatas.</summary>
    private const int MaxStoredIssues = 10_000;
    private const int MaxSampleValues = 8;

    private static readonly JsonSerializerOptions SummaryJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Error BatchNotFound = Error.NotFound("import.not_found", "Importação não encontrada.");

    /// <summary>Ordem de tentativa: do layout mais específico ao mais genérico.</summary>
    private static readonly ImportType[] DetectionOrder =
        [ImportType.Stock, ImportType.DailySales, ImportType.Transfers, ImportType.Sales, ImportType.Products, ImportType.Brands];

    public ImportType? DetectType(string filePath)
    {
        if (!SpreadsheetReader.SupportedExtensions.Contains(Path.GetExtension(filePath)))
            return null;
        SheetData sheet;
        try
        {
            sheet = SpreadsheetReader.Read(filePath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning(ex, "Não foi possível ler {File} para detectar o tipo.", filePath);
            return null;
        }
        return DetectionOrder
            .Where(type => definitions.Single(d => d.Type == type).CheckLayout(sheet) is null)
            .Select(type => (ImportType?)type)
            .FirstOrDefault();
    }

    public async Task<Result<ImportReport>> UploadAsync(
        ImportType type, Stream content, string fileName, ImportParameters parameters, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);
        if (!SpreadsheetReader.SupportedExtensions.Contains(extension))
            return Error.Validation("import.unsupported_file", "Envie um arquivo Excel (.xls, .xlsx) ou CSV.");

        var definition = definitions.Single(d => d.Type == type);
        var parametersError = ValidateParameters(definition, parameters);
        if (parametersError is not null)
            return parametersError;

        var context = new ImportContext(
            Path.GetFileName(fileName),
            definition.RequiresReferenceDate ? parameters.ReferenceDate : null,
            string.IsNullOrWhiteSpace(parameters.StoreCode) ? null : parameters.StoreCode.Trim());
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            Type = type,
            FileName = Path.GetFileName(fileName),
            UploadedByUserId = currentUser.UserId ?? Guid.Empty,
            UploadedByEmail = currentUser.Email ?? "desconhecido",
            UploadedAt = clock.GetUtcNow()
        };

        (batch.StoredFilePath, batch.FileHash, batch.FileSize) = await StoreFileAsync(batch.Id, extension, content, cancellationToken);
        batch.DuplicateOfBatchId = await db.ImportBatches
            .Where(b => b.Type == type && b.FileHash == batch.FileHash && b.Status == ImportStatus.Confirmed)
            .OrderByDescending(b => b.DecidedAt)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var outcome = await ValidateFileAsync(batch, definition, context, cancellationToken);
        if (outcome is not null)
        {
            ApplyValidationCounts(batch, outcome);
            await ApplyScopeAsync(batch, outcome.Validation.Scope, cancellationToken);
        }

        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            batch.Status == ImportStatus.Rejected ? AuditActions.ImportRejected : AuditActions.ImportUploaded,
            batch.Status == ImportStatus.Rejected ? AuditResult.Failure : AuditResult.Success,
            "ImportBatch", batch.Id.ToString(),
            Details: new
            {
                type = type.ToString(), batch.FileName, batch.TotalRows, batch.ValidRows, batch.ErrorRows, batch.RejectionReason,
                batch.ReferenceDate, store = batch.Store?.Code, batch.PeriodStart, batch.PeriodEnd
            }),
            cancellationToken);

        return BuildReport(batch);
    }

    public async Task<Result<ImportReport>> ConfirmAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await db.ImportBatches.Include(b => b.Store).SingleOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch is null)
            return BatchNotFound;
        if (batch.Status != ImportStatus.Validated)
            return Error.Conflict("import.not_pending", "Esta importação já foi decidida ou foi recusada.");

        var definition = definitions.Single(d => d.Type == batch.Type);

        // Revalida na confirmação: os cadastros podem ter mudado desde o envio (ex.: marcas importadas depois).
        var sheet = SpreadsheetReader.Read(batch.StoredFilePath);
        var context = new ImportContext(batch.FileName, batch.ReferenceDate, batch.Store?.Code);
        var validation = await definition.ValidateAsync(context, sheet, cancellationToken);
        if (validation.RejectionReason is not null)
            return Error.Conflict("import.revalidation_failed", validation.RejectionReason);

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var applied = await validation.Apply(batch.Id, cancellationToken);
            batch.ReplacedRecords = applied.ReplacedRecords;

            // As ocorrências são substituídas pelas da revalidação. Se as antigas estiverem em memória (envio e confirmação
            // no mesmo contexto), elas são soltas antes, para o EF não tentar apagá-las de novo.
            foreach (var entry in db.ChangeTracker.Entries<ImportIssue>().Where(e => e.Entity.BatchId == batch.Id).ToList())
                entry.State = EntityState.Detached;
            await db.ImportIssues.Where(i => i.BatchId == batch.Id).ExecuteDeleteAsync(cancellationToken);
            ApplyValidationCounts(batch, new ValidationOutcome(sheet.Rows.Count, validation));
            await ApplyScopeAsync(batch, validation.Scope, cancellationToken);
            batch.Status = ImportStatus.Confirmed;
            batch.DecidedAt = clock.GetUtcNow();
            batch.DecidedByEmail = currentUser.Email;
            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        await audit.LogAsync(new AuditEntry(
            AuditActions.ImportConfirmed, AuditResult.Success, "ImportBatch", batch.Id.ToString(),
            Details: new
            {
                type = batch.Type.ToString(), batch.FileName, recordsSaved = batch.ValidRows, batch.ErrorRows,
                batch.ReferenceDate, store = batch.Store?.Code, batch.PeriodStart, batch.PeriodEnd, batch.ReplacedRecords
            }),
            cancellationToken);

        logger.LogInformation("Importação {BatchId} ({Type}) confirmada: {Rows} registros gravados.", batch.Id, batch.Type, batch.ValidRows);
        return BuildReport(batch);
    }

    public async Task<Result<ImportReport>> DiscardAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await db.ImportBatches.Include(b => b.Store).SingleOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch is null)
            return BatchNotFound;
        if (batch.Status != ImportStatus.Validated)
            return Error.Conflict("import.not_pending", "Esta importação já foi decidida ou foi recusada.");

        batch.Status = ImportStatus.Discarded;
        batch.DecidedAt = clock.GetUtcNow();
        batch.DecidedByEmail = currentUser.Email;
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.ImportDiscarded, AuditResult.Success, "ImportBatch", batch.Id.ToString(),
            Details: new { type = batch.Type.ToString(), batch.FileName }), cancellationToken);

        return BuildReport(batch);
    }

    public async Task<Result<ImportReport>> GetAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await db.ImportBatches.AsNoTracking().Include(b => b.Store).SingleOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        return batch is null ? BatchNotFound : BuildReport(batch);
    }

    public async Task<PagedResult<ImportReport>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.ImportBatches.AsNoTracking().Include(b => b.Store);
        var total = await query.CountAsync(cancellationToken);
        var batches = await query
            .OrderByDescending(b => b.UploadedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ImportReport>(batches.Select(BuildReport).ToList(), page, pageSize, total);
    }

    public async Task<Result<PagedResult<ImportIssueDto>>> GetIssuesAsync(
        Guid batchId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!await db.ImportBatches.AnyAsync(b => b.Id == batchId, cancellationToken))
            return BatchNotFound;

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.ImportIssues.AsNoTracking().Where(i => i.BatchId == batchId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(i => i.RowNumber).ThenBy(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new ImportIssueDto(i.RowNumber, i.Column, i.Severity, i.Code, i.Message, i.Value))
            .ToListAsync(cancellationToken);

        return new PagedResult<ImportIssueDto>(items, page, pageSize, total);
    }

    public async IAsyncEnumerable<ImportIssueDto> StreamIssuesAsync(
        Guid batchId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var issues = db.ImportIssues.AsNoTracking()
            .Where(i => i.BatchId == batchId)
            .OrderBy(i => i.RowNumber).ThenBy(i => i.Id)
            .Select(i => new ImportIssueDto(i.RowNumber, i.Column, i.Severity, i.Code, i.Message, i.Value))
            .AsAsyncEnumerable();

        await foreach (var issue in issues.WithCancellation(cancellationToken))
            yield return issue;
    }

    /// <returns>null quando o arquivo inteiro foi recusado (o motivo fica em <see cref="ImportBatch.RejectionReason"/>).</returns>
    private async Task<ValidationOutcome?> ValidateFileAsync(
        ImportBatch batch, IImportDefinition definition, ImportContext context, CancellationToken cancellationToken)
    {
        SheetData sheet;
        try
        {
            sheet = SpreadsheetReader.Read(batch.StoredFilePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Arquivo ilegível na importação {BatchId}.", batch.Id);
            return Reject(batch, "Não foi possível ler o arquivo. Confira se é uma planilha Excel ou CSV válida.");
        }

        if (sheet.TotalLines >= SpreadsheetReader.LegacyExcelRowLimit)
            return Reject(batch, $"O arquivo tem {SpreadsheetReader.LegacyExcelRowLimit:N0} linhas, o limite de exportação do ERP: " +
                                 "ele foi cortado e está incompleto. Exporte em períodos menores.");

        var layoutProblem = definition.CheckLayout(sheet);
        if (layoutProblem is not null)
            return Reject(batch, layoutProblem);

        if (sheet.Rows.Count == 0)
            return Reject(batch, "O arquivo não tem linhas de dados.");

        var validation = await definition.ValidateAsync(context, sheet, cancellationToken);
        if (validation.RejectionReason is not null)
            return Reject(batch, validation.RejectionReason);

        batch.Status = ImportStatus.Validated;
        return new ValidationOutcome(sheet.Rows.Count, validation);
    }

    private Error? ValidateParameters(IImportDefinition definition, ImportParameters parameters)
    {
        if (!definition.RequiresReferenceDate)
            return null;
        if (parameters.ReferenceDate is null)
            return Error.Validation("import.date_missing", "Informe a data dos dados (o arquivo do ERP não traz a data).");
        if (parameters.ReferenceDate > DateOnly.FromDateTime(clock.GetLocalNow().DateTime))
            return Error.Validation("import.date_in_future", "A data dos dados não pode ser no futuro.");
        return null;
    }

    private async Task ApplyScopeAsync(ImportBatch batch, ImportScope? scope, CancellationToken cancellationToken)
    {
        scope ??= ImportScope.None;
        batch.ReferenceDate = scope.ReferenceDate;
        batch.PeriodStart = scope.PeriodStart;
        batch.PeriodEnd = scope.PeriodEnd;
        batch.StoreId = scope.StoreId;
        batch.Store = scope.StoreId is null ? null : await db.Stores.FindAsync([scope.StoreId.Value], cancellationToken);
    }

    private static ValidationOutcome? Reject(ImportBatch batch, string reason)
    {
        batch.Status = ImportStatus.Rejected;
        batch.RejectionReason = reason;
        return null;
    }

    private static void ApplyValidationCounts(ImportBatch batch, ValidationOutcome outcome)
    {
        var issues = outcome.Validation.Issues;
        batch.TotalRows = outcome.TotalRows;
        batch.ValidRows = outcome.Validation.ValidRows;
        batch.ErrorRows = issues.ErrorRowCount;
        batch.WarningCount = issues.WarningCount;
        batch.Issues = issues.All.Take(MaxStoredIssues).Select(i => { i.BatchId = batch.Id; return i; }).ToList();
        batch.IssueSummary = JsonSerializer.Serialize(Summarize(issues.All), SummaryJson);
    }

    private static List<ImportIssueGroup> Summarize(IEnumerable<ImportIssue> issues) =>
        issues
            .GroupBy(i => (i.Severity, i.Code))
            .Select(g => new ImportIssueGroup(
                g.Key.Severity,
                g.Key.Code,
                g.First().Message,
                g.Count(),
                g.Where(i => i.Value is not null).Select(i => i.Value!).Distinct().Take(MaxSampleValues).ToList()))
            .OrderBy(g => g.Severity).ThenByDescending(g => g.Count)
            .ToList();

    private async Task<(string Path, string Hash, long Size)> StoreFileAsync(
        Guid batchId, string extension, Stream content, CancellationToken cancellationToken)
    {
        var directory = Path.IsPathRooted(options.Value.StoragePath)
            ? options.Value.StoragePath
            : Path.Combine(environment.ContentRootPath, options.Value.StoragePath);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{batchId:N}{extension.ToLowerInvariant()}");
        await using (var file = File.Create(path))
            await content.CopyToAsync(file, cancellationToken);

        await using var stored = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stored, cancellationToken));
        return (path, hash, stored.Length);
    }

    private static ImportReport BuildReport(ImportBatch batch)
    {
        var groups = batch.IssueSummary is null
            ? []
            : JsonSerializer.Deserialize<List<ImportIssueGroup>>(batch.IssueSummary, SummaryJson) ?? [];
        return ToReport(batch, groups);
    }

    private static ImportReport ToReport(ImportBatch b, IReadOnlyList<ImportIssueGroup> groups) =>
        new(b.Id, b.Type, b.Status, b.FileName, b.UploadedAt, b.UploadedByEmail, b.TotalRows, b.ValidRows,
            b.ErrorRows, b.WarningCount, b.RejectionReason, b.DuplicateOfBatchId, b.DecidedAt, b.DecidedByEmail,
            b.ReferenceDate, b.Store?.Code, b.Store?.Name, b.PeriodStart, b.PeriodEnd, b.ReplacedRecords, groups);

    private sealed record ValidationOutcome(int TotalRows, ImportValidation Validation);
}

public sealed class ImportOptions
{
    public const string SectionName = "Imports";

    /// <summary>Pasta onde os arquivos originais ficam guardados (rastreabilidade). Relativa à pasta da API.</summary>
    public string StoragePath { get; set; } = "App_Data/imports";

    public long MaxFileSizeBytes { get; set; } = 120 * 1024 * 1024;
}
