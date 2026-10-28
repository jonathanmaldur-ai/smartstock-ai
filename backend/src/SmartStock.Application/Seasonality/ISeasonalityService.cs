using SmartStock.Application.Common;

namespace SmartStock.Application.Seasonality;

/// <summary>
/// Sazonalidade (decisão 65): épocas, sugestões geradas pela análise e produtos sazonais. Consulta para todos os perfis;
/// confirmar, rejeitar e editar só o Administrador. Tudo vai para a auditoria.
/// </summary>
public interface ISeasonalityService
{
    Task<IReadOnlyList<SeasonDto>> ListSeasonsAsync(CancellationToken cancellationToken = default);
    Task<Result<SeasonDto>> CreateSeasonAsync(SeasonInput input, CancellationToken cancellationToken = default);
    Task<Result<SeasonDto>> UpdateSeasonAsync(int id, SeasonInput input, CancellationToken cancellationToken = default);

    Task<PagedResult<SeasonalSuggestionDto>> ListSuggestionsAsync(SeasonalSuggestionQuery query, CancellationToken cancellationToken = default);

    /// <returns>Quantas sugestões foram decididas.</returns>
    Task<Result<int>> DecideAsync(SeasonalDecision decision, CancellationToken cancellationToken = default);

    Task<PagedResult<SeasonalProductDto>> ListProductsAsync(SeasonalProductQuery query, CancellationToken cancellationToken = default);
    Task<Result<SeasonalProductDto>> SetProductSeasonAsync(int productId, ProductSeasonInput input, CancellationToken cancellationToken = default);
}

/// <param name="InSeasonNow">A época vale hoje.</param>
public sealed record SeasonDto(
    int Id, string Name, int StartMonth, int EndMonth, bool WorldCupOnly, IReadOnlyList<string> Keywords, bool MatchDescription, bool IsActive,
    bool InSeasonNow, int ProductCount, int PendingSuggestions);

public sealed record SeasonInput(
    string Name, int StartMonth, int EndMonth, bool WorldCupOnly, IReadOnlyList<string> Keywords, bool MatchDescription, bool IsActive);

/// <param name="Stock">Estoque positivo na rede, na última análise.</param>
public sealed record SeasonalSuggestionDto(
    long Id, int ProductId, string Code, string Description, string Brand, string Category, int? SeasonId, string? SeasonName,
    string Kind, string Evidence, decimal Stock, decimal Sold12Months, DateTimeOffset UpdatedAt);

/// <param name="WithoutSeason">Só as sugestões sem época (venda parou, sem palavra-chave).</param>
public sealed record SeasonalSuggestionQuery(int? SeasonId, bool WithoutSeason, string? Kind, string? Search, int Page = 1, int PageSize = 50);

/// <summary>Sem <paramref name="Ids"/>: decide todas as pendentes do filtro (época, sem época, tipo).</summary>
public sealed record SeasonalDecision(IReadOnlyList<long> Ids, int? SeasonId, bool WithoutSeason, string? Kind, bool Confirm);

/// <param name="State">"InSeason" ou "OutOfSeason", na data de hoje.</param>
public sealed record SeasonalProductDto(
    int ProductId, string Code, string Description, string Brand, string Category, int? SeasonId, string? SeasonName,
    bool MarkedOutOfSeason, string State, decimal Stock, decimal Sold12Months);

/// <param name="OutOfSeasonOnly">Só os que estão fora da época hoje.</param>
public sealed record SeasonalProductQuery(int? SeasonId, bool OutOfSeasonOnly, string? Search, int Page = 1, int PageSize = 50);

/// <summary>Época do produto (ou nenhuma) e a marcação manual de fora de época (decisão 37).</summary>
public sealed record ProductSeasonInput(int? SeasonId, bool OutOfSeason);
