using SmartStock.Domain.Analysis;

namespace SmartStock.Tests.Domain;

/// <summary>Regras da central de alertas (decisão 47).</summary>
public sealed class AlertRulesTests
{
    private static readonly AlertRules Rules = new(new StockParameters(), id => $"{id:00} LOJA", new DateOnly(2026, 9, 23));

    private static AlertPosition Position(
        int store, decimal stock, decimal sold, StockSituation situation, decimal? coverage = null,
        decimal? before = null, decimal received = 0, decimal sent = 0, int product = 1, int brand = 1, bool warehouse = false) =>
        new(product, store, brand, warehouse, stock, sold, sold / 365m, coverage, situation, before, received, sent);

    [Fact]
    public void Vende_todo_dia_sem_reposicao_quando_o_estoque_caiu_e_nada_chegou()
    {
        var alert = Assert.Single(Rules.Evaluate([Position(6, 0, 730, StockSituation.Rupture, before: 20)]));

        Assert.Equal((AlertType.SoldWithoutReplenishment, AlertPriority.High, 6), (alert.Type, alert.Priority, alert.StoreId));
        Assert.Contains("caiu de 20 para 0", alert.Message);
    }

    [Fact]
    public void Com_transferencia_recebida_nao_e_falta_de_reposicao()
    {
        var alerts = Rules.Evaluate([Position(6, 0, 730, StockSituation.Rupture, before: 20, received: 5)]);
        Assert.DoesNotContain(alerts, a => a.Type == AlertType.SoldWithoutReplenishment);
    }

    [Fact]
    public void Queda_brusca_desconta_o_que_saiu_por_transferencia()
    {
        var explained = Rules.Evaluate([Position(6, 10, 36, StockSituation.Excess, coverage: 100, before: 100, sent: 85)]);
        var unexplained = Rules.Evaluate([Position(6, 10, 36, StockSituation.Excess, coverage: 100, before: 100, sent: 20)]);

        Assert.Empty(explained);
        var alert = Assert.Single(unexplained);
        Assert.Equal((AlertType.SharpStockDrop, AlertPriority.Medium), (alert.Type, alert.Priority));
    }

    [Fact]
    public void Queda_pequena_em_unidades_nao_e_brusca() =>
        Assert.Empty(Rules.Evaluate([Position(6, 2, 0, StockSituation.Stagnant, before: 10)]));

    [Fact]
    public void Previsao_de_ruptura_so_para_quem_vende_o_minimo_do_ano()
    {
        var relevant = Rules.Evaluate([Position(6, 5, 365, StockSituation.CriticalCoverage, coverage: 5)]);
        var lowSales = Rules.Evaluate([Position(6, 1, 10, StockSituation.CriticalCoverage, coverage: 5)]);

        Assert.Equal(AlertType.PredictedRupture, Assert.Single(relevant).Type);
        Assert.Empty(lowSales);
    }

    [Fact]
    public void Sobra_numa_loja_e_falta_em_outra_gera_um_alerta_por_produto()
    {
        var alerts = Rules.Evaluate([
            Position(18, 80, 36, StockSituation.Excess, coverage: 800),
            Position(6, 0, 730, StockSituation.Rupture),
            Position(7, 0, 365, StockSituation.Rupture),
            Position(5, 500, 0, StockSituation.Warehouse, warehouse: true)
        ]);

        var alert = Assert.Single(alerts, a => a.Type == AlertType.StoreImbalance);
        Assert.Equal(6, alert.StoreId); // a loja que mais vende entre as que faltam
        Assert.Contains("Sobra em 18 LOJA", alert.Message);
    }

    [Fact]
    public void Marca_concentrada_numa_loja_que_vende_pouco_dela()
    {
        var alerts = Rules.Evaluate([
            Position(1, 90, 10, StockSituation.Excess, coverage: 900, product: 1, brand: 7),
            Position(6, 10, 200, StockSituation.Normal, coverage: 30, product: 1, brand: 7),
            Position(5, 5000, 0, StockSituation.Warehouse, product: 1, brand: 7, warehouse: true)
        ]);

        var alert = Assert.Single(alerts, a => a.Type == AlertType.BrandConcentration);
        Assert.Equal((7, 1, AlertPriority.Low), (alert.BrandId!.Value, alert.StoreId!.Value, alert.Priority));
        Assert.StartsWith("90%", alert.Message);
    }

    [Fact]
    public void Sem_analise_anterior_so_ha_alertas_que_nao_comparam()
    {
        var alerts = Rules.Evaluate([Position(6, 0, 730, StockSituation.Rupture), Position(7, 3, 365, StockSituation.CriticalCoverage, coverage: 3)]);
        Assert.Equal([AlertType.PredictedRupture], alerts.Select(a => a.Type));
    }
}
