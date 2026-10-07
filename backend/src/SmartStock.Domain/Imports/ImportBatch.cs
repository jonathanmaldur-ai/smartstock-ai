using SmartStock.Domain.Catalog;

namespace SmartStock.Domain.Imports;

/// <summary>
/// Lote de importação: guarda de onde veio cada dado (arquivo, quem enviou, quando) e o resultado da validação.
/// Nada é gravado nos cadastros antes da confirmação do usuário.
/// </summary>
public class ImportBatch
{
    public Guid Id { get; set; }
    public ImportType Type { get; set; }
    public ImportStatus Status { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public long FileSize { get; set; }

    /// <summary>Caminho do arquivo original guardado pelo sistema (rastreabilidade).</summary>
    public string StoredFilePath { get; set; } = string.Empty;

    public Guid UploadedByUserId { get; set; }
    public string UploadedByEmail { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }

    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int ErrorRows { get; set; }
    public int WarningCount { get; set; }

    /// <summary>Motivo quando o arquivo inteiro é recusado (layout errado, arquivo cortado...).</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Outro lote confirmado com o mesmo arquivo (mesmo hash), se houver.</summary>
    public Guid? DuplicateOfBatchId { get; set; }

    /// <summary>
    /// Resumo de todas as ocorrências por tipo (JSON), calculado antes do limite de ocorrências gravadas individualmente.
    /// </summary>
    public string? IssueSummary { get; set; }

    /// <summary>Data dos dados informada pelo usuário (estoque e vendas: o arquivo do ERP não traz data).</summary>
    public DateOnly? ReferenceDate { get; set; }

    /// <summary>Loja do arquivo (vendas: um arquivo por loja).</summary>
    public int? StoreId { get; set; }
    public Store? Store { get; set; }

    /// <summary>Período coberto pelos dados (vendas: 12 meses até a data; transferências: primeira à última data).</summary>
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }

    /// <summary>Registros de importações anteriores substituídos na confirmação (transferências do mesmo período).</summary>
    public int ReplacedRecords { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedByEmail { get; set; }

    public List<ImportIssue> Issues { get; set; } = [];
}

public class ImportIssue
{
    public long Id { get; set; }
    public Guid BatchId { get; set; }
    public int RowNumber { get; set; }
    public string? Column { get; set; }
    public ImportIssueSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public enum ImportType
{
    Brands = 1,
    Products = 2,
    Stock = 3,
    Sales = 4,
    Transfers = 5,

    /// <summary>Vendas por dia de cada loja (decisão 51): uma linha por venda no arquivo, guardadas como total por dia.</summary>
    DailySales = 6
}

public enum ImportStatus
{
    /// <summary>Validado, aguardando o usuário confirmar ou descartar.</summary>
    Validated = 1,
    Confirmed = 2,
    Discarded = 3,

    /// <summary>Arquivo recusado por inteiro (não pode ser confirmado).</summary>
    Rejected = 4
}

public enum ImportIssueSeverity
{
    /// <summary>A linha não será gravada.</summary>
    Error = 1,

    /// <summary>A linha será gravada, mas merece atenção (ex.: categoria nova).</summary>
    Warning = 2
}
