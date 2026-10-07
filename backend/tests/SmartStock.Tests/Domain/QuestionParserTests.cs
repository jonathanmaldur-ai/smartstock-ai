using SmartStock.Infrastructure.Assistant;

namespace SmartStock.Tests.Domain;

/// <summary>Interpretação das perguntas do chat (decisão 41).</summary>
public sealed class QuestionParserTests
{
    private static readonly KnownStore Matriz = new(1, "01", "Matriz");
    private static readonly KnownStore Deposito = new(5, "05", "Depósito");
    private static readonly KnownStore Buriti = new(9, "09", "Buriti Shopping");
    private static readonly KnownStore Sjc = new(10, "10", "São José dos Campos");
    private static readonly KnownStore Taubate = new(11, "11", "Taubaté");

    private static readonly QuestionParser Parser = new(
        [Matriz, Deposito, Buriti, Sjc, Taubate],
        [new KnownBrand(1, "MATTEL"), new KnownBrand(2, "FANTASMINHA"), new KnownBrand(3, "MARCA 036600 (SEM CADASTRO)")]);

    [Fact]
    public void Ruptura_numa_loja_pelo_apelido()
    {
        var q = Parser.Parse("Quais produtos estão em ruptura no Buriti?");
        Assert.Equal((QuestionTopic.Ruptures, Buriti), (q.Topic, q.Store));
        Assert.Null(q.Search);
    }

    [Fact]
    public void Transferencia_reconhece_origem_e_destino()
    {
        var q = Parser.Parse("o que transferir do depósito para taubaté");
        Assert.Equal((QuestionTopic.Transfers, Deposito, Taubate), (q.Topic, q.Origin, q.Destination));
    }

    [Fact]
    public void Marca_e_loja_por_codigo()
    {
        var q = Parser.Parse("negativos da mattel na loja 01");
        Assert.Equal((QuestionTopic.Negatives, Matriz), (q.Topic, q.Store));
        Assert.Equal("MATTEL", q.Brand?.Name);
    }

    [Fact]
    public void Produto_sem_assunto_vira_onde_tem()
    {
        var q = Parser.Parse("onde tem estalo de salão?");
        Assert.Equal((QuestionTopic.ProductStock, "ESTALO SALAO"), (q.Topic, q.Search));
    }

    [Fact]
    public void Top_10_nao_e_a_loja_10_e_sjc_e_apelido()
    {
        Assert.Null(Parser.Parse("top 10 mais vendidos").Store);
        Assert.Equal(Sjc, Parser.Parse("mais vendidos em SJC").Store);
    }

    [Fact]
    public void Marcas_com_nome_de_loja_ou_palavra_comum_nao_confundem()
    {
        var parser = new QuestionParser(
            [new KnownStore(6, "06", "Mogi Mirim")],
            [new KnownBrand(1, "MOGI"), new KnownBrand(2, "GERAL"), new KnownBrand(3, "MATTEL")]);

        var mogi = parser.Parse("rupturas em Mogi Mirim");
        Assert.Equal("06", mogi.Store?.Code);
        Assert.Null(mogi.Brand);
        Assert.Null(parser.Parse("resumo geral").Brand);
    }

    [Fact]
    public void Pergunta_sem_nada_reconhecido_mostra_ajuda()
    {
        Assert.Equal(QuestionTopic.Help, Parser.Parse("oi").Topic);
        Assert.Equal(QuestionTopic.Summary, Parser.Parse("como está a matriz?").Topic);
    }
}
