using SmartStock.Domain.Analysis;

namespace SmartStock.Tests.Domain;

/// <summary>Regras da seção 2 das decisões (decisão 8) e venda mínima da decisão 35.</summary>
public sealed class TransferPlannerTests
{
    private const int Deposito = 5, MogiGuacu = 1, Pinda = 18, Taubate = 11;

    private static readonly StockParameters Defaults = new();
    private static readonly TransferPlanner Planner = new(new StockRules(Defaults));

    private static StoreStock Store(int id, decimal stock, decimal sold12Months) => new(id, $"Loja {id:00}", false, stock, sold12Months);
    private static StoreStock Warehouse(decimal stock) => new(Deposito, "05 Depósito", true, stock, 0);

    private static TransferPlan Plan(params StoreStock[] stores) => Planner.Plan(stores, [], daysSinceStock: 0);

    [Fact]
    public void Exemplo_do_CLAUDE_md_leva_o_destino_ao_ideal_sem_deixar_a_origem_abaixo_do_ideal()
    {
        // Mogi Guaçu: 8 un., vende 3/dia (1.095 no ano). Pindamonhangaba: 80 un., vende pouco (73 no ano = 0,2/dia).
        var plan = Plan(Store(MogiGuacu, 8, 1095), Store(Pinda, 80, 73));

        var transfer = Assert.Single(plan.Transfers);
        Assert.Equal((Pinda, MogiGuacu), (transfer.OriginStoreId, transfer.DestinationStoreId));
        Assert.Equal(74, transfer.Quantity); // Pinda fica com 6 un. = 30 dias de 0,2/dia
        Assert.Equal(AlertPriority.High, transfer.Priority); // 2,7 dias de cobertura
        Assert.Contains("abaixo do mínimo de 15", transfer.Reason);

        var purchase = Assert.Single(plan.Purchases);
        Assert.Equal((MogiGuacu, 8m), (purchase.StoreId, purchase.Quantity)); // precisava de 82 para 30 dias
    }

    [Fact]
    public void Deposito_e_a_primeira_origem_e_depois_a_loja_com_maior_cobertura()
    {
        var plan = Plan(Store(MogiGuacu, 0, 365), Warehouse(10), Store(Taubate, 100, 365), Store(Pinda, 200, 365));

        Assert.Equal([Deposito, Pinda], plan.Transfers.Select(t => t.OriginStoreId));
        Assert.Equal([10m, 20m], plan.Transfers.Select(t => t.Quantity));
        Assert.Equal(AlertPriority.Critical, plan.Transfers[0].Priority);
        Assert.Empty(plan.Purchases);
    }

    [Fact]
    public void Loja_com_estoque_negativo_nunca_e_origem_e_destino_negativo_e_sinalizado()
    {
        var plan = Plan(Store(MogiGuacu, -4, 365), Store(Pinda, -50, 0), Store(Taubate, 60, 0));

        var transfer = Assert.Single(plan.Transfers);
        Assert.Equal(Taubate, transfer.OriginStoreId);
        Assert.True(transfer.DestinationNegative);
    }

    [Fact]
    public void Produto_que_vende_menos_que_o_minimo_nao_gera_sugestao_e_fica_com_prioridade_baixa()
    {
        var plan = Plan(Store(MogiGuacu, 0, 11), Warehouse(100));

        Assert.Empty(plan.Transfers);
        Assert.Empty(plan.Purchases);
        Assert.Equal(AlertPriority.Low, new StockRules(Defaults).Evaluate(false, 0, 11, 0).Priority);
    }

    [Fact]
    public void Transferencia_aprovada_a_caminho_desconta_a_falta_do_destino()
    {
        var inTransit = new[] { new InTransit(Deposito, MogiGuacu, 10) };

        var plan = Planner.Plan([Store(MogiGuacu, 0, 365), Warehouse(100)], inTransit, daysSinceStock: 0);

        Assert.Equal(20, Assert.Single(plan.Transfers).Quantity); // 30 dias de 1/dia, com 10 já a caminho
    }

    [Fact]
    public void Estoque_e_projetado_pelos_dias_desde_a_importacao()
    {
        var metrics = new StockRules(Defaults).Evaluate(false, stock: 20, sold12Months: 365, daysSinceStock: 10);

        Assert.Equal(10, metrics.ProjectedStock);
        Assert.Equal(StockSituation.BelowMinimum, metrics.Situation);
    }

    [Fact]
    public void Classificacao_segue_as_faixas_da_secao_2()
    {
        var rules = new StockRules(Defaults);
        Assert.Equal(StockSituation.Rupture, rules.Evaluate(false, 0, 365, 0).Situation);
        Assert.Equal(StockSituation.CriticalCoverage, rules.Evaluate(false, 6, 365, 0).Situation);
        Assert.Equal(StockSituation.Normal, rules.Evaluate(false, 30, 365, 0).Situation);
        Assert.Equal(StockSituation.Excess, rules.Evaluate(false, 121, 365, 0).Situation);
        Assert.Equal(StockSituation.Stagnant, rules.Evaluate(false, 5, 0, 0).Situation);
        Assert.Equal(StockSituation.Warehouse, rules.Evaluate(true, 500, 0, 0).Situation);
    }

    [Fact]
    public void Parametros_fora_de_ordem_sao_recusados()
    {
        Assert.Null(new StockParameters().Validate());
        Assert.NotNull(new StockParameters { MinimumDays = 40 }.Validate());
    }
}
