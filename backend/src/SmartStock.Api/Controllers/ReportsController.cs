using Microsoft.AspNetCore.Mvc;
using SmartStock.Api.Common;
using SmartStock.Api.Exports;
using SmartStock.Application.Common;
using SmartStock.Application.Reports;

namespace SmartStock.Api.Controllers;

/// <summary>Central de relatórios (Fase 4, decisão 42): consulta e download para todos os perfis.</summary>
[ApiController]
[Route("api/reports")]
public sealed class ReportsController(IReportService reports, IMonthlyReportService monthly, IDailySalesReportService daily) : ControllerBase
{
    /// <summary>Prévia na tela e versão de impressão (PDF): limitadas; Excel e CSV trazem tudo.</summary>
    private const int PreviewRows = 500;

    [HttpGet]
    public Task<ReportCatalog> Catalog(CancellationToken cancellationToken) => reports.CatalogAsync(cancellationToken);

    /// <summary>Relatório mensal: a análise atual comparada com uma anterior (a escolhida, ou a mais recente com estoque anterior).</summary>
    [HttpGet("mensal")]
    public async Task<ActionResult<MonthlyReport>> Monthly([FromQuery] Guid? comparar, CancellationToken cancellationToken) =>
        this.ToActionResult(await monthly.BuildAsync(comparar, cancellationToken));

    [HttpGet("mensal/excel")]
    public async Task<IActionResult> MonthlyExcel([FromQuery] Guid? comparar, CancellationToken cancellationToken)
    {
        var result = await monthly.BuildAsync(comparar, cancellationToken);
        return result.IsSuccess
            ? File(MonthlyReportWorkbook.Build(result.Value), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"relatorio-mensal-{result.Value.Current.StockDate:yyyy-MM-dd}.xlsx")
            : this.ToProblem(result.Error!);
    }

    /// <summary>Vendas por dia e por loja (decisão 51). Sem período: os últimos 30 dias com venda importada.</summary>
    [HttpGet("vendas-diarias")]
    public Task<DailySalesReport> DailySales(
        [FromQuery] DateOnly? de, [FromQuery] DateOnly? ate, [FromQuery] int? storeId, CancellationToken cancellationToken) =>
        daily.BuildAsync(de, ate, storeId, 100, cancellationToken);

    [HttpGet("vendas-diarias/excel")]
    public async Task<IActionResult> DailySalesExcel(
        [FromQuery] DateOnly? de, [FromQuery] DateOnly? ate, [FromQuery] int? storeId, CancellationToken cancellationToken)
    {
        var report = await daily.BuildAsync(de, ate, storeId, null, cancellationToken);
        var productDays = await daily.ProductDaysAsync(new DailySaleProductDayQuery(de, ate, storeId, null, PageSize: null), cancellationToken);
        var saleLines = await daily.AllSaleLinesAsync(de, ate, storeId, cancellationToken);
        return File(DailySalesWorkbook.Build(report, productDays.Items, saleLines), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"vendas-por-dia-{report.From:yyyy-MM-dd}-a-{report.To:yyyy-MM-dd}.xlsx");
    }

    /// <summary>As vendas em que um produto saiu no período, numa loja ou na rede (decisão 53); até 500.</summary>
    [HttpGet("vendas-diarias/produtos/vendas")]
    public Task<IReadOnlyList<SaleLineDetail>> DailySalesProductSales(
        [FromQuery] int productId, [FromQuery] DateOnly de, [FromQuery] DateOnly ate, [FromQuery] int? storeId, CancellationToken cancellationToken) =>
        daily.SaleLinesAsync(storeId, de, ate, productId, cancellationToken);

    /// <summary>Produtos vendidos em cada dia e loja (decisão 52), paginado.</summary>
    [HttpGet("vendas-diarias/produtos")]
    public Task<PagedResult<DailySaleProductDay>> DailySalesProducts(
        [FromQuery] DateOnly? de, [FromQuery] DateOnly? ate, [FromQuery] int? storeId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        daily.ProductDaysAsync(new DailySaleProductDayQuery(de, ate, storeId, search, page, pageSize), cancellationToken);

    [HttpGet("{key}")]
    public async Task<ActionResult<ReportData>> Preview(string key, [FromQuery] ReportFilterQuery filter, CancellationToken cancellationToken) =>
        this.ToActionResult(await reports.BuildAsync(key, filter.ToFilter(), PreviewRows, cancellationToken));

    [HttpGet("{key}/excel")]
    public async Task<IActionResult> Excel(string key, [FromQuery] ReportFilterQuery filter, CancellationToken cancellationToken)
    {
        var result = await reports.BuildAsync(key, filter.ToFilter(), null, cancellationToken);
        return result.IsSuccess
            ? File(ReportExport.Excel(result.Value), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ReportExport.FileName(result.Value, "xlsx"))
            : this.ToProblem(result.Error!);
    }

    [HttpGet("{key}/csv")]
    public async Task<IActionResult> Csv(string key, [FromQuery] ReportFilterQuery filter, CancellationToken cancellationToken)
    {
        var result = await reports.BuildAsync(key, filter.ToFilter(), null, cancellationToken);
        return result.IsSuccess
            ? File(ReportExport.Csv(result.Value), "text/csv; charset=utf-8", ReportExport.FileName(result.Value, "csv"))
            : this.ToProblem(result.Error!);
    }
}

public sealed record ReportFilterQuery(int? StoreId, int? BrandId, int? CategoryId)
{
    public ReportFilter ToFilter() => new(StoreId, BrandId, CategoryId);
}
