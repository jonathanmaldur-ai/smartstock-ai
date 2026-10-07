using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStock.Api.Common;
using SmartStock.Api.Exports;
using SmartStock.Api.Security;
using SmartStock.Application.Analysis;
using SmartStock.Application.Common;
using SmartStock.Domain.Analysis;

namespace SmartStock.Api.Controllers;

/// <summary>Análise do estoque (Módulo 2.3). Consulta para todos os perfis; gerar e alterar parâmetros têm permissão própria.</summary>
[ApiController]
[Route("api/analysis")]
public sealed class AnalysisController(IStockAnalysisService analysis, IAnalysisQueryService queries) : ControllerBase
{
    [HttpGet]
    public Task<AnalysisSummary> Summary(CancellationToken cancellationToken) => analysis.GetSummaryAsync(cancellationToken);

    /// <summary>Calcula uma nova análise com o estoque e as vendas atuais. Sugestões pendentes anteriores são substituídas.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.CanAnalyze)]
    public async Task<ActionResult<AnalysisSummary>> Run(CancellationToken cancellationToken) =>
        this.ToActionResult(await analysis.RunAsync(cancellationToken));

    [HttpGet("parameters")]
    public Task<StockParametersDto> Parameters(CancellationToken cancellationToken) => analysis.GetParametersAsync(cancellationToken);

    [HttpPut("parameters")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<StockParametersDto>> UpdateParameters(ParametersBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await analysis.UpdateParametersAsync(
            new StockParametersInput(body.CriticalCoverageDays, body.MinimumDays, body.IdealDays, body.MaximumDays, body.ExcessDays, body.MinimumAnnualSales,
                body.SendAlertEmail),
            cancellationToken));

    [HttpGet("positions")]
    public Task<PagedResult<PositionDto>> Positions(
        [FromQuery] StockSituation? situation, [FromQuery] int? storeId, [FromQuery] int? brandId, [FromQuery] int? categoryId,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        queries.ListPositionsAsync(new PositionQuery(situation, storeId, brandId, categoryId, search, page, pageSize), cancellationToken);

    [HttpGet("purchases")]
    public Task<PagedResult<PurchaseDto>> Purchases(
        [FromQuery] int? storeId, [FromQuery] int? brandId, [FromQuery] int? categoryId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        queries.ListPurchasesAsync(new PurchaseQuery(storeId, brandId, categoryId, search, page, pageSize), cancellationToken);
}

/// <summary>Painel de estoque negativo (decisões 26 e 38): consulta para todos os perfis.</summary>
[ApiController]
[Route("api/analysis/negatives")]
public sealed class NegativeStockController(INegativeStockQueryService negatives) : ControllerBase
{
    [HttpGet("summary")]
    public Task<NegativeSummary> Summary(CancellationToken cancellationToken) => negatives.GetSummaryAsync(cancellationToken);

    [HttpGet("ranking")]
    public Task<IReadOnlyList<NegativeGroupDto>> Ranking(
        [FromQuery] NegativeGrouping by = NegativeGrouping.Brand, [FromQuery] int? storeId = null, CancellationToken cancellationToken = default) =>
        negatives.RankingAsync(by, storeId, cancellationToken);

    [HttpGet]
    public Task<PagedResult<NegativeItemDto>> List([FromQuery] NegativeFilter filter, CancellationToken cancellationToken) =>
        negatives.ListAsync(filter.ToQuery(), cancellationToken);

    /// <summary>Excel para corrigir no ERP.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] NegativeFilter filter, CancellationToken cancellationToken) =>
        File(
            NegativesWorkbook.Build(await negatives.ListAllAsync(filter.ToQuery(), cancellationToken)),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"estoque-negativo-{DateTime.Now:yyyy-MM-dd}.xlsx");
}

public sealed record NegativeFilter(
    int? StoreId, AlertPriority? Priority, NegativeCause? Cause, int? BrandId, int? CategoryId, string? Search, int Page = 1, int PageSize = 25)
{
    public NegativeQuery ToQuery() => new(StoreId, Priority, Cause, BrandId, CategoryId, Search, Page, PageSize);
}

/// <summary>Sugestões de transferência: o sistema recomenda, o usuário decide, nada é executado (CLAUDE.md).</summary>
[ApiController]
[Route("api/transfer-suggestions")]
public sealed class TransferSuggestionsController(IStockAnalysisService analysis, IAnalysisQueryService queries) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SuggestionDto>> List([FromQuery] SuggestionFilter filter, CancellationToken cancellationToken) =>
        queries.ListSuggestionsAsync(filter.ToQuery(), cancellationToken);

    /// <summary>Sugestões agrupadas por rota (origem → destino), decisão 36.</summary>
    [HttpGet("routes")]
    public Task<IReadOnlyList<SuggestionRouteDto>> Routes([FromQuery] SuggestionFilter filter, CancellationToken cancellationToken) =>
        queries.ListRoutesAsync(filter.ToQuery(), cancellationToken);

    /// <summary>Todas as sugestões do filtro em Excel.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] SuggestionFilter filter, CancellationToken cancellationToken)
    {
        var suggestions = await queries.ListAllSuggestionsAsync(filter.ToQuery(), cancellationToken);
        return File(
            SuggestionsWorkbook.Build(suggestions),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"sugestoes-transferencia-{DateTime.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>Aprova ou rejeita sugestões pendentes (uma ou várias).</summary>
    [HttpPost("decision")]
    [Authorize(Policy = Policies.CanApprove)]
    public async Task<ActionResult<DecisionResult>> Decide(DecisionBody body, CancellationToken cancellationToken)
    {
        var route = body.OriginStoreId is { } origin && body.DestinationStoreId is { } destination
            ? new SuggestionRoute(origin, destination, body.HideNegativeDestination)
            : null;
        var result = await analysis.DecideAsync(new SuggestionDecision(body.Ids ?? [], body.Approve, body.Note, route), cancellationToken);
        return result.IsSuccess ? new DecisionResult(result.Value) : this.ToProblem(result.Error!);
    }
}

public sealed record SuggestionFilter(
    SuggestionStatus? Status, int? OriginStoreId, int? DestinationStoreId, AlertPriority? Priority,
    int? BrandId, int? CategoryId, string? Search, int Page = 1, int PageSize = 25, bool HideNegativeDestination = false)
{
    public SuggestionQuery ToQuery() =>
        new(Status, OriginStoreId, DestinationStoreId, Priority, BrandId, CategoryId, Search, Page, PageSize, HideNegativeDestination);
}

/// <summary>Ids das sugestões, ou a rota inteira (origem e destino) quando não há ids.</summary>
public sealed record DecisionBody(
    [MaxLength(10_000)] List<long>? Ids,
    bool Approve,
    [MaxLength(500)] string? Note,
    int? OriginStoreId = null,
    int? DestinationStoreId = null,
    bool HideNegativeDestination = false);

public sealed record DecisionResult(int Decided);

public sealed record ParametersBody(
    [Range(1, 365)] int CriticalCoverageDays,
    [Range(1, 365)] int MinimumDays,
    [Range(1, 365)] int IdealDays,
    [Range(1, 365)] int MaximumDays,
    [Range(1, 3650)] int ExcessDays,
    [Range(0, 100_000)] decimal MinimumAnnualSales,
    bool SendAlertEmail = false);

/// <summary>Central de alertas (decisão 47): só avisa, nunca executa nada (CLAUDE.md).</summary>
[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(IAlertService alerts) : ControllerBase
{
    [HttpGet("summary")]
    public Task<AlertSummary> Summary(CancellationToken cancellationToken) => alerts.GetSummaryAsync(cancellationToken);

    [HttpGet]
    public Task<PagedResult<AlertDto>> List([FromQuery] AlertFilter filter, CancellationToken cancellationToken) =>
        alerts.ListAsync(filter.ToQuery(), cancellationToken);

    /// <summary>"Já vi" é só uma marcação de leitura: qualquer perfil que analisa pode usar.</summary>
    [HttpPost("seen")]
    [Authorize(Policy = Policies.CanAnalyze)]
    public async Task<ActionResult<DecisionResult>> MarkSeen([FromBody] AlertSeenRequest body, CancellationToken cancellationToken)
    {
        var result = await alerts.MarkSeenAsync(body.Ids ?? [], body.Seen, cancellationToken);
        return result.IsSuccess ? new DecisionResult(result.Value) : this.ToProblem(result.Error!);
    }
}

public sealed record AlertFilter(
    AlertType? Type, AlertPriority? Priority, int? StoreId, int? BrandId, string? Search, bool IncludeSeen = false, int Page = 1, int PageSize = 25)
{
    public AlertQuery ToQuery() => new(Type, Priority, StoreId, BrandId, Search, IncludeSeen, Page, PageSize);
}

public sealed record AlertSeenRequest(IReadOnlyList<long>? Ids, bool Seen = true);
