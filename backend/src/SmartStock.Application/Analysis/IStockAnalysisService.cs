using SmartStock.Application.Common;
using SmartStock.Domain.Analysis;

namespace SmartStock.Application.Analysis;

/// <summary>
/// Análise do estoque (Módulo 2.3): situação de cada produto-loja e sugestões de transferência e compra.
/// O sistema só recomenda; a decisão é do usuário e nada é executado (CLAUDE.md).
/// </summary>
public interface IStockAnalysisService
{
    /// <summary>Calcula uma nova análise com a foto de estoque e as vendas atuais. Sugestões pendentes anteriores são substituídas.</summary>
    Task<Result<AnalysisSummary>> RunAsync(CancellationToken cancellationToken = default);

    Task<AnalysisSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<StockParametersDto> GetParametersAsync(CancellationToken cancellationToken = default);
    Task<Result<StockParametersDto>> UpdateParametersAsync(StockParametersInput input, CancellationToken cancellationToken = default);

    /// <summary>Aprova ou rejeita sugestões pendentes. Retorna quantas foram decididas.</summary>
    Task<Result<int>> DecideAsync(SuggestionDecision decision, CancellationToken cancellationToken = default);
}

public interface IAnalysisQueryService
{
    Task<PagedResult<PositionDto>> ListPositionsAsync(PositionQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<SuggestionDto>> ListSuggestionsAsync(SuggestionQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SuggestionRouteDto>> ListRoutesAsync(SuggestionQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<PurchaseDto>> ListPurchasesAsync(PurchaseQuery query, CancellationToken cancellationToken = default);

    /// <summary>Todas as sugestões do filtro (sem paginação), para o Excel.</summary>
    Task<IReadOnlyList<SuggestionDto>> ListAllSuggestionsAsync(SuggestionQuery query, CancellationToken cancellationToken = default);
}

/// <param name="RelevantRuptures">Rupturas de produto que vende o suficiente para sugestão (decisão 35).</param>
/// <param name="IsOutdated">Há estoque ou vendas importados depois desta análise: vale gerar outra.</param>
public sealed record AnalysisSummary(
    Guid? AnalysisId,
    DateOnly? StockDate,
    DateOnly? AnalysisDate,
    DateTimeOffset? CreatedAt,
    string? CreatedByEmail,
    bool IsOutdated,
    IReadOnlyList<SituationCount> Situations,
    int RelevantRuptures,
    SuggestionTotals Suggestions,
    int PurchaseCount,
    decimal PurchaseUnits);

public sealed record SituationCount(StockSituation Situation, int Count);

public sealed record SuggestionTotals(int Pending, decimal PendingUnits, int Approved, decimal ApprovedUnits, int Rejected, int Completed);

public sealed record StockParametersDto(
    int CriticalCoverageDays,
    int MinimumDays,
    int IdealDays,
    int MaximumDays,
    int ExcessDays,
    decimal MinimumAnnualSales,
    DateTimeOffset UpdatedAt,
    string? UpdatedByEmail,
    bool SendAlertEmail);

public sealed record StockParametersInput(
    int CriticalCoverageDays, int MinimumDays, int IdealDays, int MaximumDays, int ExcessDays, decimal MinimumAnnualSales,
    bool SendAlertEmail = false);

/// <param name="Route">Sem <paramref name="Ids"/>: decide todas as pendentes da rota (origem → destino).</param>
public sealed record SuggestionDecision(IReadOnlyList<long> Ids, bool Approve, string? Note, SuggestionRoute? Route = null);

public sealed record SuggestionRoute(int OriginStoreId, int DestinationStoreId, bool HideNegativeDestination);

public sealed record PositionQuery(
    StockSituation? Situation, int? StoreId, int? BrandId, int? CategoryId, string? Search, int Page = 1, int PageSize = 25);

public sealed record PositionDto(
    int ProductId,
    string ProductCode,
    string ProductDescription,
    string BrandName,
    string CategoryName,
    string StoreCode,
    string StoreName,
    decimal Stock,
    decimal ProjectedStock,
    decimal Sold12Months,
    decimal DailyAverage,
    decimal? CoverageDays,
    StockSituation Situation,
    AlertPriority Priority);

public sealed record SuggestionQuery(
    SuggestionStatus? Status,
    int? OriginStoreId,
    int? DestinationStoreId,
    AlertPriority? Priority,
    int? BrandId,
    int? CategoryId,
    string? Search,
    int Page = 1,
    int PageSize = 25,
    bool HideNegativeDestination = false);

/// <summary>Sugestões agrupadas por rota (decisão 36): a lista de separação de cada carga.</summary>
public sealed record SuggestionRouteDto(
    int OriginStoreId,
    string OriginCode,
    string OriginName,
    int DestinationStoreId,
    string DestinationCode,
    string DestinationName,
    int Count,
    decimal Units,
    int CriticalCount,
    int NegativeCount,
    IReadOnlyList<RouteProductPreview> TopProducts);

/// <summary>Prévia dos produtos mais urgentes da rota, para ver o que vai na carga sem abrir a lista.</summary>
public sealed record RouteProductPreview(string Description, decimal Quantity);

public sealed record SuggestionDto(
    long Id,
    int ProductId,
    string ProductCode,
    string ProductDescription,
    string? ProductReference,
    string BrandName,
    string CategoryName,
    string OriginCode,
    string OriginName,
    string DestinationCode,
    string DestinationName,
    decimal Quantity,
    AlertPriority Priority,
    SuggestionStatus Status,
    string Reason,
    decimal OriginStock,
    decimal OriginDailyAverage,
    decimal? OriginCoverageDays,
    decimal DestinationStock,
    decimal DestinationDailyAverage,
    decimal? DestinationCoverageDays,
    decimal? DestinationCoverageAfter,
    bool DestinationNegative,
    DateOnly AnalysisDate,
    DateTimeOffset? DecidedAt,
    string? DecidedByEmail,
    string? DecisionNote,
    DateOnly? CompletedOn,
    decimal? CompletedQuantity);

public sealed record PurchaseQuery(int? StoreId, int? BrandId, int? CategoryId, string? Search, int Page = 1, int PageSize = 25);

public sealed record PurchaseDto(
    int ProductId,
    string ProductCode,
    string ProductDescription,
    string BrandName,
    string CategoryName,
    string StoreCode,
    string StoreName,
    decimal Quantity,
    decimal Stock,
    decimal DailyAverage,
    decimal? CoverageDays);
