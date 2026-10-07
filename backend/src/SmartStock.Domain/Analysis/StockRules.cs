using SmartStock.Domain.Inventory;

namespace SmartStock.Domain.Analysis;

/// <summary>Métricas calculadas de um produto numa loja.</summary>
public sealed record PositionMetrics(
    decimal ProjectedStock,
    decimal DailyAverage,
    decimal? CoverageDays,
    StockSituation Situation,
    AlertPriority Priority)
{
    /// <summary>Estoque usado nos cálculos: negativo conta como zero (seção 2).</summary>
    public decimal EffectiveStock => Math.Max(ProjectedStock, 0);
}

/// <summary>Classificação da seção 2 (decisão 8), com a venda mínima da decisão 35 e o estoque projetado da decisão 18.</summary>
public sealed class StockRules(StockParameters parameters)
{
    public StockParameters Parameters => parameters;

    public PositionMetrics Evaluate(bool isWarehouse, decimal stock, decimal sold12Months, int daysSinceStock)
    {
        var dailyAverage = isWarehouse ? 0 : StockMetrics.DailyAverage(sold12Months);
        var projected = stock - dailyAverage * Math.Max(daysSinceStock, 0);
        var coverage = StockMetrics.CoverageDays(projected, dailyAverage);

        if (isWarehouse)
            return new PositionMetrics(projected, 0, null, StockSituation.Warehouse, AlertPriority.None);

        var situation = Classify(projected, dailyAverage, coverage);
        return new PositionMetrics(projected, dailyAverage, coverage, situation, PriorityOf(situation, sold12Months));
    }

    /// <summary>Vende o suficiente para ruptura crítica e sugestão de transferência (decisão 35).</summary>
    public bool IsRelevant(decimal sold12Months) => sold12Months > 0 && sold12Months >= parameters.MinimumAnnualSales;

    private StockSituation Classify(decimal projected, decimal dailyAverage, decimal? coverage)
    {
        if (dailyAverage <= 0)
            return projected > 0 ? StockSituation.Stagnant : StockSituation.NoMovement;
        if (projected <= 0)
            return StockSituation.Rupture;
        if (coverage < parameters.CriticalCoverageDays)
            return StockSituation.CriticalCoverage;
        if (coverage < parameters.MinimumDays)
            return StockSituation.BelowMinimum;
        if (coverage > parameters.ExcessDays)
            return StockSituation.Excess;
        return StockSituation.Normal;
    }

    private AlertPriority PriorityOf(StockSituation situation, decimal sold12Months)
    {
        var lowTurnover = !IsRelevant(sold12Months);
        return situation switch
        {
            StockSituation.Rupture => lowTurnover ? AlertPriority.Low : AlertPriority.Critical,
            StockSituation.CriticalCoverage => lowTurnover ? AlertPriority.Low : AlertPriority.High,
            StockSituation.BelowMinimum => lowTurnover ? AlertPriority.Low : AlertPriority.Medium,
            StockSituation.Excess => AlertPriority.Medium,
            StockSituation.Stagnant => AlertPriority.Low,
            _ => AlertPriority.None
        };
    }
}
