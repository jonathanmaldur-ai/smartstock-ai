using System.Text.RegularExpressions;
using SmartStock.Domain.Catalog;

namespace SmartStock.Infrastructure.Assistant;

/// <summary>Assunto da pergunta (decisão 41).</summary>
public enum QuestionTopic
{
    Help,
    Summary,
    Ruptures,
    Negatives,
    Transfers,
    Purchases,
    Stagnant,
    Excess,
    ProductStock,
    TopSellers
}

public sealed record KnownStore(int Id, string Code, string Name);
public sealed record KnownBrand(int Id, string Name);

/// <param name="Search">Palavras que sobraram (produto), ou null.</param>
public sealed record ParsedQuestion(
    QuestionTopic Topic,
    KnownStore? Store,
    KnownStore? Origin,
    KnownStore? Destination,
    KnownBrand? Brand,
    string? Search);

/// <summary>
/// Interpreta perguntas em português sobre o estoque, sem IA (decisão 41): assunto por palavras-chave,
/// loja por nome/código/apelido, marca pelo nome e o que sobra como busca de produto.
/// </summary>
public sealed partial class QuestionParser(IReadOnlyList<KnownStore> stores, IReadOnlyList<KnownBrand> brands)
{
    /// <summary>Assuntos na ordem de prioridade: o primeiro que casar vence.</summary>
    private static readonly (QuestionTopic Topic, string[] Keys)[] Topics =
    [
        (QuestionTopic.Help, ["AJUDA", "O QUE VOCE FAZ", "O QUE VOCE SABE", "COMO USAR", "EXEMPLO"]),
        (QuestionTopic.Negatives, ["NEGATIV"]),
        (QuestionTopic.Purchases, ["COMPRA", "COMPRAR", "PEDIR PARA O FORNECEDOR", "FORNECEDOR"]),
        (QuestionTopic.Transfers, ["TRANSFER", "MANDAR", "ENVIAR", "REMANEJ", "REDISTRIBU", "SUGEST", "LEVAR PARA", "SEPARAR"]),
        (QuestionTopic.Ruptures, ["RUPTURA", "EM FALTA", "FALTANDO", "FALTA ", "SEM ESTOQUE", "ZERADO", "ACABOU", "ACABANDO"]),
        (QuestionTopic.Stagnant, ["PARADO", "SEM VENDA", "NAO VENDE", "NAO VENDEU", "ENCALHAD"]),
        (QuestionTopic.Excess, ["EXCESSO", "SOBRANDO", "SOBRA", "MUITO ESTOQUE", "ESTOQUE ALTO"]),
        (QuestionTopic.TopSellers, ["MAIS VENDID", "MAIS VENDE", "TOP ", "CAMPEAO", "CAMPEOES", "MELHORES"]),
        (QuestionTopic.ProductStock, ["ONDE TEM", "ONDE ESTA", "QUANTO TEM", "QUANTOS TEM", "ESTOQUE DE", "ESTOQUE DO", "ESTOQUE DA", "TEM ESTOQUE", "FICHA"]),
        (QuestionTopic.Summary, ["RESUMO", "COMO ESTA", "COMO ESTAO", "SITUACAO", "VISAO GERAL", "PANORAMA", "INDICADOR"])
    ];

    /// <summary>Apelidos além dos nomes cadastrados (a chave é o código da loja).</summary>
    private static readonly Dictionary<string, string[]> StoreAliases = new()
    {
        ["01"] = ["MATRIZ", "MOGI GUACU"],
        ["05"] = ["DEPOSITO", "CD"],
        ["09"] = ["BURITI"],
        ["10"] = ["SJC", "SAO JOSE"],
        ["12"] = ["ECOMMERCE", "E-COMMERCE", "SITE", "LOJA VIRTUAL"],
        ["17"] = ["CASA E DECOR", "CASA DECOR", "DECOR"],
        ["18"] = ["PINDA", "PINDAMONHANGABA"]
    };

    /// <summary>Palavras sem valor de busca (artigos, verbos da pergunta, palavras de assunto).</summary>
    private static readonly HashSet<string> StopWords =
    [
        "A", "O", "AS", "OS", "UM", "UMA", "DE", "DO", "DA", "DOS", "DAS", "NO", "NA", "NOS", "NAS", "EM", "PARA", "PRA", "PRO", "POR",
        "COM", "SEM", "E", "OU", "QUE", "QUAL", "QUAIS", "QUANTO", "QUANTOS", "QUANTAS", "ONDE", "COMO", "ESTA", "ESTAO", "TEM", "TEMOS",
        "HA", "ME", "MOSTRA", "MOSTRE", "MOSTRAR", "LISTA", "LISTAR", "VER", "QUERO", "SABER", "PRODUTO", "PRODUTOS", "ITEM", "ITENS",
        "LOJA", "LOJAS", "ESTOQUE", "ESTOQUES", "MARCA", "HOJE", "AGORA", "DEVO", "PRECISO", "FAZER", "SOBRE", "ISSO", "ESSE", "ESSA",
        "NEGATIVO", "NEGATIVOS", "RUPTURA", "RUPTURAS", "FALTA", "FALTANDO", "TRANSFERIR", "TRANSFERENCIA", "TRANSFERENCIAS", "MANDAR",
        "ENVIAR", "COMPRAR", "COMPRA", "COMPRAS", "PARADO", "PARADOS", "EXCESSO", "SOBRANDO", "VENDIDO", "VENDIDOS", "MAIS", "RESUMO",
        "SITUACAO", "SUGESTAO", "SUGESTOES", "ZERADO", "ZERADOS", "VENDE", "VENDEU", "VENDA", "VENDAS", "NAO", "TOP", "GERAL", "REDE",
        "FICHA", "SEMANA", "MES", "ACABOU", "ACABANDO", "ENCALHADO", "ENCALHADOS", "SOBRA", "REMANEJAR", "REDISTRIBUIR", "LEVAR", "SEPARAR",
        "OI", "OLA", "BOM", "BOA", "DIA", "TARDE", "NOITE", "TUDO", "BEM", "OBRIGADO", "OBRIGADA", "VALEU", "POR FAVOR", "FAVOR"
    ];

    private const int MinBrandLength = 4;

    [GeneratedRegex(@"\b(\d{1,2})\b")]
    private static partial Regex StoreNumber();

    public ParsedQuestion Parse(string question)
    {
        var text = " " + CatalogText.Key(question).Replace('?', ' ').Replace('!', ' ').Replace(',', ' ').Replace('.', ' ') + " ";
        QuestionTopic? matched = Topics.Where(t => t.Keys.Any(k => text.Contains(k))).Select(t => (QuestionTopic?)t.Topic).FirstOrDefault();

        var mentions = FindStores(text);
        var destination = mentions.FirstOrDefault(m => m.Role == Role.Destination)?.Store;
        var origin = mentions.FirstOrDefault(m => m.Role == Role.Origin)?.Store;
        var store = mentions.FirstOrDefault()?.Store;

        // A marca é procurada sem o nome da loja: existe a marca "MOGI", que não pode casar com "Mogi Mirim".
        var withoutStores = RemoveMentions(text, mentions.Select(m => m.Matched));
        var brand = FindBrand(withoutStores);
        var remaining = RemoveMentions(withoutStores, [brand is null ? null : CatalogText.Key(brand.Name)]);
        var search = string.Join(' ', remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !StopWords.Contains(w) && w.Length > 1));
        if (string.IsNullOrWhiteSpace(search))
            search = null;

        // Sem assunto reconhecido: um produto citado vira "onde tem"; só loja ou marca vira resumo.
        var topic = matched
            ?? (search is not null ? QuestionTopic.ProductStock
                : store is not null || brand is not null ? QuestionTopic.Summary
                : QuestionTopic.Help);

        return new ParsedQuestion(topic, store, origin, destination, brand, search);
    }

    private enum Role { General, Origin, Destination }

    private sealed record StoreMention(KnownStore Store, string Matched, int Position, Role Role);

    private List<StoreMention> FindStores(string text)
    {
        var mentions = new List<StoreMention>();
        foreach (var store in stores)
        {
            var names = new List<string> { CatalogText.Key(store.Name) };
            if (StoreAliases.TryGetValue(store.Code, out var aliases)) names.AddRange(aliases);
            foreach (var name in names.Distinct().OrderByDescending(n => n.Length))
            {
                var index = text.IndexOf(" " + name + " ", StringComparison.Ordinal);
                if (index < 0) continue;
                mentions.Add(new StoreMention(store, name, index, RoleBefore(text, index)));
                break;
            }
        }

        // "loja 09", "na 06", "para 11" ou "09": número de loja cadastrada. "Top 10" não é a loja 10.
        foreach (Match match in StoreNumber().Matches(text))
        {
            var previous = text[..match.Index].Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            var digits = match.Groups[1].Value;
            if (!(previous is "LOJA" or "NA" or "NO" or "DA" or "DO" or "DE" or "PARA" or "PRA" or "PRO" or "EM" || digits.StartsWith('0')))
                continue;
            var code = digits.PadLeft(2, '0');
            var store = stores.FirstOrDefault(s => s.Code == code);
            if (store is not null && mentions.All(m => m.Store.Id != store.Id))
                mentions.Add(new StoreMention(store, match.Groups[1].Value, match.Index, RoleBefore(text, match.Index)));
        }
        return mentions.OrderBy(m => m.Position).ToList();
    }

    /// <summary>"para o Buriti" é destino; "do Depósito" é origem.</summary>
    private static Role RoleBefore(string text, int index)
    {
        var before = text[..index].Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeLast(3).ToArray();
        if (before.Any(w => w is "PARA" or "PRA" or "PRO" or "PARA:")) return Role.Destination;
        if (before.LastOrDefault(w => w is not ("LOJA" or "O" or "A")) is "DE" or "DO" or "DA") return Role.Origin;
        return Role.General;
    }

    private KnownBrand? FindBrand(string text) =>
        brands
            .Where(b => b.Name.Length >= MinBrandLength && !b.Name.Contains("SEM CADASTRO", StringComparison.OrdinalIgnoreCase))
            .Select(b => (Brand: b, Key: CatalogText.Key(b.Name)))
            // Marca com nome de palavra comum de pergunta (ex.: "GERAL") não é reconhecida sozinha.
            .Where(b => !StopWords.Contains(b.Key) && text.Contains(" " + b.Key + " ", StringComparison.Ordinal))
            .OrderByDescending(b => b.Key.Length)
            .Select(b => b.Brand)
            .FirstOrDefault();

    private static string RemoveMentions(string text, IEnumerable<string?> mentions)
    {
        foreach (var mention in mentions.Where(m => !string.IsNullOrEmpty(m)))
            text = text.Replace(" " + mention + " ", " ", StringComparison.Ordinal);
        return text;
    }
}
