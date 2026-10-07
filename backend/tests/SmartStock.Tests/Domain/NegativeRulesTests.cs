using SmartStock.Domain.Analysis;

namespace SmartStock.Tests.Domain;

/// <summary>Critérios do painel de negativos (decisão 38).</summary>
public sealed class NegativeRulesTests
{
    private static NegativeFacts Facts(
        decimal quantity = -5, decimal sold = 0, bool warehouse = false, string? unit = "PC", decimal pending = 0,
        bool received = true, bool shipped = false, string? duplicate = null) =>
        new(quantity, sold, warehouse, unit, pending, received, shipped, duplicate);

    [Theory]
    [InlineData(-1, 12, false, AlertPriority.Critical)]
    [InlineData(-10, 0, false, AlertPriority.High)]
    [InlineData(-3, 0, false, AlertPriority.Medium)]
    [InlineData(-2, 0, false, AlertPriority.Low)]
    [InlineData(-500, 0, true, AlertPriority.High)]
    public void Prioridade_segue_a_decisao_38(decimal quantity, decimal sold, bool warehouse, AlertPriority expected) =>
        Assert.Equal(expected, NegativeRules.Priority(Facts(quantity, sold, warehouse)));

    [Fact]
    public void Transferencia_enviada_e_nao_recebida()
    {
        Assert.True(NegativeRules.Causes(Facts(pending: 12)).HasFlag(NegativeCause.TransferNotReceived));
    }

    [Fact]
    public void Loja_que_vende_sem_nunca_receber_por_transferencia()
    {
        Assert.Equal(NegativeCause.SaleWithoutEntry, NegativeRules.Causes(Facts(sold: 40, received: false)));
    }

    [Fact]
    public void Deposito_que_enviou_sem_entrada_de_nota()
    {
        Assert.Equal(NegativeCause.ShippedWithoutEntry, NegativeRules.Causes(Facts(warehouse: true, received: false, shipped: true)));
    }

    [Fact]
    public void Fracionado_e_duplicado_sao_apontados()
    {
        var causes = NegativeRules.Causes(Facts(quantity: -2.5m, duplicate: "789000"));
        Assert.True(causes.HasFlag(NegativeCause.FractionalUnit));
        Assert.True(causes.HasFlag(NegativeCause.PossibleDuplicate));
        Assert.True(NegativeRules.Causes(Facts(unit: "MT")).HasFlag(NegativeCause.FractionalUnit));
    }
}
