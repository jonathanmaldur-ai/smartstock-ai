using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStock.Api.Common;
using SmartStock.Api.Security;
using SmartStock.Application.Common;
using SmartStock.Application.Seasonality;

namespace SmartStock.Api.Controllers;

/// <summary>Sazonalidade (decisão 65): consulta para todos os perfis; confirmar, rejeitar e editar só o Administrador.</summary>
[ApiController]
[Route("api/seasonality")]
public sealed class SeasonalityController(ISeasonalityService seasonality) : ControllerBase
{
    [HttpGet("seasons")]
    public Task<IReadOnlyList<SeasonDto>> Seasons(CancellationToken cancellationToken) => seasonality.ListSeasonsAsync(cancellationToken);

    [HttpPost("seasons")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<SeasonDto>> CreateSeason(SeasonBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await seasonality.CreateSeasonAsync(body.ToInput(), cancellationToken));

    [HttpPut("seasons/{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<SeasonDto>> UpdateSeason(int id, SeasonBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await seasonality.UpdateSeasonAsync(id, body.ToInput(), cancellationToken));

    /// <summary>Sugestões pendentes, geradas a cada análise.</summary>
    [HttpGet("suggestions")]
    public Task<PagedResult<SeasonalSuggestionDto>> Suggestions(
        [FromQuery] int? seasonId, [FromQuery] bool withoutSeason, [FromQuery] string? kind, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        seasonality.ListSuggestionsAsync(new SeasonalSuggestionQuery(seasonId, withoutSeason, kind, search, page, pageSize), cancellationToken);

    /// <summary>Confirma ou rejeita as selecionadas, ou todas as pendentes do filtro quando não vier nenhuma.</summary>
    [HttpPost("suggestions/decision")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<DecisionResult>> Decide(SeasonalDecisionBody body, CancellationToken cancellationToken)
    {
        var result = await seasonality.DecideAsync(
            new SeasonalDecision(body.Ids ?? [], body.SeasonId, body.WithoutSeason, body.Kind, body.Confirm), cancellationToken);
        return result.IsSuccess ? new DecisionResult(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("products")]
    public Task<PagedResult<SeasonalProductDto>> Products(
        [FromQuery] int? seasonId, [FromQuery] bool outOfSeasonOnly, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        seasonality.ListProductsAsync(new SeasonalProductQuery(seasonId, outOfSeasonOnly, search, page, pageSize), cancellationToken);

    [HttpPut("products/{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<SeasonalProductDto>> SetProductSeason(int id, ProductSeasonBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await seasonality.SetProductSeasonAsync(id, new ProductSeasonInput(body.SeasonId, body.OutOfSeason), cancellationToken));
}

public sealed record SeasonBody(
    [Required(ErrorMessage = "Informe o nome."), MaxLength(60)] string Name,
    [Range(1, 12)] int StartMonth,
    [Range(1, 12)] int EndMonth,
    bool WorldCupOnly,
    [Required] IReadOnlyList<string> Keywords,
    bool MatchDescription,
    bool IsActive)
{
    public SeasonInput ToInput() => new(Name, StartMonth, EndMonth, WorldCupOnly, Keywords, MatchDescription, IsActive);
}

public sealed record SeasonalDecisionBody(IReadOnlyList<long>? Ids, int? SeasonId, bool WithoutSeason, string? Kind, bool Confirm);

public sealed record ProductSeasonBody(int? SeasonId, bool OutOfSeason);
