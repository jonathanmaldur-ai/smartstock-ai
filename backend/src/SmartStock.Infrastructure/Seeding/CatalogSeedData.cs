using SmartStock.Domain.Catalog;

namespace SmartStock.Infrastructure.Seeding;

/// <summary>
/// Carga inicial dos cadastros, conforme DECISOES_DO_PROJETO.md (decisões 4, 22 e 23).
/// Depois de criados, lojas e categorias são mantidas pelo Administrador na plataforma.
/// </summary>
internal static class CatalogSeedData
{
    public static readonly IReadOnlyList<Store> Stores =
    [
        new("01", "Matriz", "Mogi Guaçu", StoreType.Store, StoreStatus.Active),
        new("04", "Jaguariúna", "Jaguariúna", StoreType.Store, StoreStatus.Active),
        new("05", "Depósito", null, StoreType.Warehouse, StoreStatus.Active),
        new("06", "Mogi Mirim", "Mogi Mirim", StoreType.Store, StoreStatus.Active),
        new("07", "Itapira", "Itapira", StoreType.Store, StoreStatus.Active),
        new("08", "Limeira", "Limeira", StoreType.Store, StoreStatus.Active),
        new("09", "Buriti Shopping", "Mogi Guaçu", StoreType.Store, StoreStatus.Active),
        new("10", "São José dos Campos", "São José dos Campos", StoreType.Store, StoreStatus.Active),
        new("11", "Taubaté", "Taubaté", StoreType.Store, StoreStatus.Active),
        new("12", "E-commerce", null, StoreType.Ecommerce, StoreStatus.Active),
        new("17", "Casa e Decor", "Mogi Guaçu", StoreType.Store, StoreStatus.Active),
        new("18", "Pindamonhangaba", "Pindamonhangaba", StoreType.Store, StoreStatus.Active),
        new("02", "Loja 02 (fechada)", null, StoreType.Store, StoreStatus.Closed),
        new("03", "Loja 03 (fechada)", null, StoreType.Store, StoreStatus.Closed),
        new("13", "Loja 13 (fechada)", null, StoreType.Store, StoreStatus.Closed),
        new("14", "Loja 14 (fechada)", null, StoreType.Store, StoreStatus.Closed),
        new("15", "Loja 15 (fechada)", null, StoreType.Store, StoreStatus.Closed),
        new("16", "Loja 16 (fechada)", null, StoreType.Store, StoreStatus.Closed)
    ];

    /// <summary>Categoria oficial → grafias encontradas nas planilhas (primeira versão, revisável pelo Administrador).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> CategoryAliases = new Dictionary<string, string[]>
    {
        ["BRINQUEDOS"] = ["BRINQUEDO", "BRIQUEDOS", "BRNQUEDOS", "BRIINQUEDOS", "BRINQIEDOS"],
        ["ALIMENTÍCIO"] = ["ALIMENTICO", "ALIMENTOS"],
        ["BEBIDAS"] = ["BEBIDA", "BEBIBA", "BEBEIDA"],
        ["FANTASIAS"] = ["FANTASIA"],
        ["HALLOWEEN"] = ["HALLOWEEM", "ARTIGOS P/ HALL"],
        ["CARTAS DE BARALHO"] = ["CARTA DE BARALH", "CARTAS DE BARAL"],
        ["FESTA"] = ["FESTAS", "ARTIGOS DE FEST", "ARTIGOS FESTA", "ART FESTAS"],
        ["DISNEY"] = ["DISENY"],
        ["BABY"] = ["BABAY", "BEBE"],
        ["CARNAVAL"] = ["CAARNAVAL"],
        ["ESCOLAR"] = ["ESCOALR"],
        ["EMBALAGEM"] = ["EMBALAGENS"],
        ["PIJAMAS"] = ["PIJAMA"],
        ["PISCINA"] = ["PISCINAS"],
        ["FIGURINHAS"] = ["FIGURINHAS COPA"],
        ["IMPORTAÇÃO"] = ["IMPORTACAO", "IMPORTADOS"],
        ["VALE PRESENTE"] = ["VALES PRESENTES"],
        ["PRODUTOS USO LOJA"] = ["PRODUTOS USO LO", "LOJA"],
        ["RELÓGIO DE PONTO"] = ["RELOGIO DE PONT"]
    };

    /// <summary>
    /// Épocas de venda (decisão 65), aprovadas pelo responsável em 28/10/2026: nome, meses e palavras-chave.
    /// Criadas só se a tabela estiver vazia; depois, o Administrador edita na tela Sazonalidade.
    /// </summary>
    /// Volta às aulas não procura na descrição: "ônibus escolar" e "ache encaixe escolar" são brinquedos do ano todo.
    public static readonly IReadOnlyList<(string Name, int Start, int End, bool WorldCupOnly, bool MatchDescription, string[] Keywords)> Seasons =
    [
        ("Copa do Mundo", 5, 7, true, true, ["FIGURINHAS", "COPA", "COPA DO MUNDO"]),
        ("Páscoa", 2, 4, false, true, ["PASCOA", "OVO DE PASCOA"]),
        ("Natal", 10, 12, false, true, ["NATAL", "NATALINO", "NATALINA", "PAPAI NOEL"]),
        ("Carnaval", 1, 2, false, true, ["CARNAVAL"]),
        ("Festa Junina", 5, 7, false, true, ["FESTA JUNINA", "JUNINA", "JUNINO", "ESTALOS"]),
        ("Halloween", 9, 10, false, true, ["HALLOWEEN", "HALLOWEEM"]),
        ("Volta às aulas", 12, 3, false, false, ["ESCOLAR"])
    ];

    /// <summary>Fora das análises: itens que não são mercadoria e bebidas. Alimentícios são mantidos (decisão 23).</summary>
    public static readonly IReadOnlyList<string> ExcludedCategories =
    [
        "VALE PRESENTE", "FRETE", "PRODUTOS USO LOJA", "EXPOSITOR", "GONDOLA", "DISPLAY", "EMBALAGEM", "SACOLA",
        "IMPRESSORA", "COMPUTADORES", "RELÓGIO DE PONTO", "AVARIAS",
        "AGUA", "BEBIDAS", "REFRIGERANTE", "ENERGETICO"
    ];
}
