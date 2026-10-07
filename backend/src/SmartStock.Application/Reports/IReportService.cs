using SmartStock.Application.Common;

namespace SmartStock.Application.Reports;

/// <summary>Central de relatórios (Fase 4, decisões 11 e 42): dados da análise mais recente, com filtros.</summary>
public interface IReportService
{
    Task<ReportCatalog> CatalogAsync(CancellationToken cancellationToken = default);

    /// <param name="maxRows">Limite de linhas (prévia na tela); null devolve tudo (Excel e CSV).</param>
    Task<Result<ReportData>> BuildAsync(string key, ReportFilter filter, int? maxRows, CancellationToken cancellationToken = default);
}

public sealed record ReportCatalog(IReadOnlyList<ReportInfo> Reports, IReadOnlyList<ReportOption> Brands);

public sealed record ReportInfo(string Key, string Title, string Description, string Group);

public sealed record ReportOption(int Id, string Name);

public sealed record ReportFilter(int? StoreId, int? BrandId, int? CategoryId);

public enum ReportColumnKind
{
    Text,
    Integer,
    Decimal,
    Date
}

public sealed record ReportColumn(string Header, ReportColumnKind Kind);

/// <param name="FilterSummary">Filtros aplicados em texto (vai no cabeçalho do Excel e da impressão).</param>
/// <param name="Rows">Valores: texto, número ou data (DateOnly), na ordem das colunas.</param>
public sealed record ReportData(
    string Key,
    string Title,
    string Description,
    string FilterSummary,
    DateOnly? AnalysisDate,
    DateOnly? StockDate,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int TotalRows);
