using SmartStock.Domain.Catalog;

namespace SmartStock.Application.Analysis;

/// <summary>Dashboard executivo (Módulo 2.4, decisão 39): visão geral da rede a partir da análise mais recente.</summary>
public interface IDashboardService
{
    Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default);
}

/// <param name="Kpis">null enquanto não houver análise gerada.</param>
public sealed record DashboardDto(
    DataFreshness Freshness,
    DashboardKpis? Kpis,
    IReadOnlyList<StoreDashboardRow> Stores,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<TrendPoint> Trend);

/// <summary>Idade dos dados (decisões 18 e 19): estoque com mais de 7 dias, vendas ou transferências com mais de 30.</summary>
public sealed record DataFreshness(
    DateOnly? StockDate,
    int? StockAgeDays,
    bool StockOutdated,
    DateOnly? SalesDate,
    int? SalesAgeDays,
    bool SalesOutdated,
    DateOnly? TransfersDate,
    int? TransfersAgeDays,
    bool TransfersOutdated);

/// <param name="AnnualTurnover">Venda de 12 meses ÷ estoque total (vezes por ano).</param>
/// <param name="NetworkCoverageDays">Estoque total ÷ venda média diária da rede.</param>
public sealed record DashboardKpis(
    DateOnly AnalysisDate,
    DateOnly StockDate,
    decimal StockUnits,
    decimal Sold12Months,
    decimal? AnnualTurnover,
    decimal? NetworkCoverageDays,
    int RelevantRuptures,
    int PendingSuggestions,
    decimal PendingUnits,
    int NegativeItems);

/// <param name="BelowMinimum">Inclui a cobertura crítica (menos de 7 dias).</param>
public sealed record StoreDashboardRow(
    int StoreId,
    string Code,
    string Name,
    StoreType Type,
    decimal StockUnits,
    decimal Sold12Months,
    decimal? CoverageDays,
    int Rupture,
    int BelowMinimum,
    int Normal,
    int Excess,
    int Stagnant);

/// <param name="RuptureStores">Lojas em que o produto está em ruptura e vende o suficiente para sugestão (decisão 35).</param>
public sealed record TopProductDto(int ProductId, string Code, string Description, string BrandName, decimal Sold12Months, decimal StockUnits, int RuptureStores);

public sealed record TrendPoint(DateOnly AnalysisDate, DateOnly StockDate, int RelevantRuptures, int NegativeItems, int Suggestions);
