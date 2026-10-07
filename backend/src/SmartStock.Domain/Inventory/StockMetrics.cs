namespace SmartStock.Domain.Inventory;

/// <summary>Fórmulas base da seção 2 das decisões do projeto (decisão 8).</summary>
public static class StockMetrics
{
    /// <summary>O arquivo de vendas é o acumulado de 12 meses.</summary>
    public const int SalesWindowDays = 365;

    /// <summary>Venda Média Diária = quantidade vendida em 12 meses ÷ 365.</summary>
    public static decimal DailyAverage(decimal soldIn12Months) =>
        soldIn12Months <= 0 ? 0 : soldIn12Months / SalesWindowDays;

    /// <summary>
    /// Cobertura em dias = estoque ÷ VMD. Estoque negativo conta como zero (o alerta de inconsistência é à parte).
    /// Sem venda, não há cobertura (null).
    /// </summary>
    public static decimal? CoverageDays(decimal stock, decimal dailyAverage) =>
        dailyAverage <= 0 ? null : Math.Max(stock, 0) / dailyAverage;
}
