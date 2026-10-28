using SmartStock.Domain.Analysis;
using SmartStock.Domain.Catalog;

namespace SmartStock.Tests.Domain;

/// <summary>Sazonalidade (decisão 65).</summary>
public sealed class SeasonalRulesTests
{
    private static Season Season(int id, string name, int start, int end, bool worldCupOnly = false, params string[] keywords) =>
        new() { Id = id, Name = name, StartMonth = start, EndMonth = end, WorldCupOnly = worldCupOnly, Keywords = global::SmartStock.Domain.Catalog.Season.NormalizeKeywords(keywords) };

    [Theory]
    [InlineData(10, 12, "2026-11-15", true)]
    [InlineData(10, 12, "2026-09-30", false)]
    [InlineData(12, 3, "2027-01-10", true)] // atravessa o ano
    [InlineData(12, 3, "2026-07-01", false)]
    public void Epoca_vale_nos_meses_do_intervalo(int start, int end, string date, bool expected) =>
        Assert.Equal(expected, Season(1, "X", start, end).IsInSeason(DateOnly.Parse(date)));

    [Fact]
    public void Copa_so_vale_em_ano_de_copa()
    {
        var copa = Season(1, "Copa do Mundo", 5, 7, worldCupOnly: true);

        Assert.True(copa.IsInSeason(new DateOnly(2026, 6, 15)));
        Assert.False(copa.IsInSeason(new DateOnly(2027, 6, 15)));
        Assert.True(copa.IsInSeason(new DateOnly(2030, 6, 15)));
    }

    [Fact]
    public void Estado_respeita_a_marcacao_manual_e_a_epoca_inativa()
    {
        var natal = Season(1, "Natal", 10, 12);
        var outubro = new DateOnly(2026, 10, 28);

        Assert.Equal(SeasonState.InSeason, SeasonalRules.StateOf(natal, false, outubro));
        Assert.Equal(SeasonState.OutOfSeason, SeasonalRules.StateOf(natal, false, new DateOnly(2027, 2, 1)));
        Assert.Equal(SeasonState.OutOfSeason, SeasonalRules.StateOf(null, true, outubro));
        natal.IsActive = false;
        Assert.Equal(SeasonState.None, SeasonalRules.StateOf(natal, false, outubro));
    }

    [Fact]
    public void Palavra_chave_casa_palavra_inteira_sem_acento_na_categoria_ou_descricao()
    {
        Season[] seasons = [Season(1, "Natal", 10, 12, false, "NATAL", "PAPAI NOEL"), Season(2, "Páscoa", 2, 4, false, "PÁSCOA")];

        Assert.Equal("Categoria NATAL", SeasonalRules.MatchKeyword(seasons, "NATAL", null, "BOLA")!.Value.Evidence);
        Assert.Equal(1, SeasonalRules.MatchKeyword(seasons, "DECORACAO", null, "BONECO PAPAI NOEL 30CM")!.Value.Season.Id);
        Assert.Equal(2, SeasonalRules.MatchKeyword(seasons, "ALIMENTICIO", "PASCOA", "OVO")!.Value.Season.Id);
        Assert.Equal(1, SeasonalRules.MatchKeyword(seasons, "DECORACAO", null, "ENFEITE/NATAL")!.Value.Season.Id);
        Assert.Null(SeasonalRules.MatchKeyword(seasons, "BONECAS", null, "BONECA NATALIA")); // não é a palavra NATAL
    }

    [Fact]
    public void Epoca_sem_busca_na_descricao_so_olha_categoria_e_subcategoria()
    {
        var aulas = Season(1, "Volta às aulas", 12, 3, false, "ESCOLAR");
        aulas.MatchDescription = false;

        Assert.Null(SeasonalRules.MatchKeyword([aulas], "BRINQUEDOS", null, "BEBE MUSICAL ONIBUS ESCOLAR"));
        Assert.NotNull(SeasonalRules.MatchKeyword([aulas], "ESCOLAR", null, "AGENDA ANUAL"));
        Assert.NotNull(SeasonalRules.MatchKeyword([aulas], "DISNEY", "ESCOLAR", "AGENDA MICKEY"));
    }

    [Fact]
    public void Parou_de_vender_quando_vendia_bem_e_quase_nada_na_janela()
    {
        Assert.True(SeasonalRules.HasStopped(2532, 3));
        Assert.False(SeasonalRules.HasStopped(2532, 400)); // vende normalmente
        Assert.False(SeasonalRules.HasStopped(30, 0)); // venda pequena demais para concluir
    }

    [Fact]
    public void Na_epoca_vale_a_maior_venda_entre_12_meses_e_30_dias_anualizados()
    {
        Assert.Equal(1825m, SeasonalRules.PeakSold(365, 150)); // 150 em 30 dias = 5/dia
        Assert.Equal(365m, SeasonalRules.PeakSold(365, 10));
    }

    [Fact]
    public void Fora_da_epoca_vira_sobra_de_epoca_sem_prioridade_e_falta_nao_e_ruptura()
    {
        var excess = new PositionMetrics(500, 0.1m, 5000, StockSituation.Excess, AlertPriority.Medium);
        var rupture = new PositionMetrics(0, 7, 0, StockSituation.Rupture, AlertPriority.Critical);
        var warehouse = new PositionMetrics(100, 0, null, StockSituation.Warehouse, AlertPriority.None);

        Assert.Equal((StockSituation.OutOfSeason, AlertPriority.None), (SeasonalRules.AsOutOfSeason(excess).Situation, SeasonalRules.AsOutOfSeason(excess).Priority));
        Assert.Equal(StockSituation.NoMovement, SeasonalRules.AsOutOfSeason(rupture).Situation);
        Assert.Equal(StockSituation.Warehouse, SeasonalRules.AsOutOfSeason(warehouse).Situation);
    }
}
