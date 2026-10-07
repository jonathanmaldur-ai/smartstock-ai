using SmartStock.Domain.Inventory;

namespace SmartStock.Tests.Domain;

/// <summary>Fórmulas base da decisão 8.</summary>
public sealed class StockMetricsTests
{
    [Fact]
    public void Vmd_e_a_venda_de_12_meses_dividida_por_365()
    {
        Assert.Equal(2m, StockMetrics.DailyAverage(730));
        Assert.Equal(0m, StockMetrics.DailyAverage(0));
        Assert.Equal(0m, StockMetrics.DailyAverage(-5));
    }

    [Fact]
    public void Cobertura_e_o_estoque_dividido_pela_vmd()
    {
        // Exemplo do CLAUDE.md: 8 unidades vendendo 3 por dia cobrem cerca de 2,6 dias.
        Assert.Equal(2.67m, Math.Round(StockMetrics.CoverageDays(8, 3)!.Value, 2));
    }

    [Fact]
    public void Estoque_negativo_conta_como_zero_e_sem_venda_nao_ha_cobertura()
    {
        Assert.Equal(0m, StockMetrics.CoverageDays(-10, 2));
        Assert.Null(StockMetrics.CoverageDays(50, 0));
    }
}
