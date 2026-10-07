using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStock.Api.Common;
using SmartStock.Api.Security;
using SmartStock.Application.Common;
using SmartStock.Application.Imports;
using SmartStock.Domain.Imports;

namespace SmartStock.Api.Controllers;

[ApiController]
[Route("api/imports")]
[Authorize(Policy = Policies.CanImport)]
public sealed class ImportsController(IImportService imports) : ControllerBase
{
    /// <summary>O estoque bruto do ERP (formato Excel 4.0) passa de 60 MB.</summary>
    private const long MaxUploadBytes = 120 * 1024 * 1024;

    /// <summary>
    /// Envia e valida o arquivo. Nada é gravado até a confirmação.
    /// Estoque e vendas exigem a data dos dados; vendas aceitam a loja (sem ela, vem do nome da aba ou do arquivo).
    /// </summary>
    [HttpPost("{type}")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<ActionResult<ImportReport>> Upload(
        ImportType type, IFormFile? file, [FromForm] DateOnly? referenceDate, [FromForm] string? storeCode,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return this.ToProblem(Error.Validation("import.file_missing", "Selecione um arquivo."));

        await using var stream = file.OpenReadStream();
        var parameters = new ImportParameters(referenceDate, storeCode);
        return this.ToActionResult(await imports.UploadAsync(type, stream, file.FileName, parameters, cancellationToken));
    }

    [HttpGet]
    public Task<PagedResult<ImportReport>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        imports.ListAsync(page, pageSize, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ImportReport>> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await imports.GetAsync(id, cancellationToken));

    [HttpGet("{id:guid}/issues")]
    public async Task<ActionResult<PagedResult<ImportIssueDto>>> Issues(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        this.ToActionResult(await imports.GetIssuesAsync(id, page, pageSize, cancellationToken));

    /// <summary>Ocorrências em CSV (separador ";" e UTF-8 com BOM, para abrir direto no Excel em português).</summary>
    [HttpGet("{id:guid}/issues.csv")]
    public async Task Download(Guid id, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"ocorrencias-{id:N}.csv\"";

        await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync("Linha;Coluna;Tipo;Código;Mensagem;Valor");
        await foreach (var issue in imports.StreamIssuesAsync(id, cancellationToken))
        {
            var severity = issue.Severity == ImportIssueSeverity.Error ? "Erro" : "Aviso";
            await writer.WriteLineAsync(
                $"{issue.RowNumber};{Csv(issue.Column)};{severity};{Csv(issue.Code)};{Csv(issue.Message)};{Csv(issue.Value)}");
        }
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<ImportReport>> Confirm(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await imports.ConfirmAsync(id, cancellationToken));

    [HttpPost("{id:guid}/discard")]
    public async Task<ActionResult<ImportReport>> Discard(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await imports.DiscardAsync(id, cancellationToken));

    /// <summary>Escapa para CSV e neutraliza fórmulas (=, +, -, @) que o Excel executaria ao abrir.</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if ("=+-@".Contains(value[0]))
            value = "'" + value;
        return value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
