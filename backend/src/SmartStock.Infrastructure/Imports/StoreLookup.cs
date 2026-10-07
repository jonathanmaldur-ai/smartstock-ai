using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Catalog;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports;

/// <summary>Unidades da rede por código ("06"), para reconhecer lojas nas planilhas (decisão 4).</summary>
internal sealed class StoreLookup
{
    private readonly Dictionary<string, Store> _byCode;

    private StoreLookup(IEnumerable<Store> stores) => _byCode = stores.ToDictionary(s => s.Code, StringComparer.Ordinal);

    public static async Task<StoreLookup> LoadAsync(SmartStockDbContext db, CancellationToken cancellationToken) =>
        new(await db.Stores.AsNoTracking().ToListAsync(cancellationToken));

    /// <summary>Aceita "6", "06" ou "06 MOGI MIRIM".</summary>
    public Store? Find(string? codeOrName)
    {
        var code = Store.NormalizeCode(codeOrName);
        return code is not null && _byCode.TryGetValue(code, out var store) ? store : null;
    }

    public Store? FindById(int id) => _byCode.Values.FirstOrDefault(s => s.Id == id);

    /// <summary>Palavras que não distinguem uma loja de outra (ex.: "Buriti Shopping" é reconhecida só por "BURITI").</summary>
    private static readonly HashSet<string> GenericWords = ["DE", "DA", "DO", "DOS", "DAS", "E", "SHOPPING", "LOJA"];

    /// <summary>Abreviação aceita: pelo menos 4 letras do começo da palavra ("PINDA" = Pindamonhangaba).</summary>
    private const int MinimumAbbreviation = 4;

    /// <summary>
    /// Loja ativa pelo nome, quando o arquivo não traz o código (ex.: "MOGI MIRIM.XLS", "SHOPPINH BURITI.XLS", "PINDA.XLS"):
    /// cada palavra que distingue o nome da loja aparece no texto (sem acento e sem espaço) ou está abreviada nele.
    /// Só devolve se exatamente uma loja combinar; ambíguo ou nenhuma, null.
    /// </summary>
    public Store? FindByName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var compact = Compact(text);
        var textWords = Words(text).Where(w => w.Length >= MinimumAbbreviation).ToList();
        bool Matches(string word) => compact.Contains(word) || textWords.Any(word.StartsWith);
        var matches = _byCode.Values
            .Where(s => s.IsActive)
            .Where(s => DistinctiveWords(s.Name) is { Count: > 0 } words && words.All(Matches))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static IEnumerable<string> Words(string text) =>
        CatalogText.Key(text).Split([' ', '-', '.', '/', '_'], StringSplitOptions.RemoveEmptyEntries);

    private static List<string> DistinctiveWords(string name) =>
        CatalogText.Key(name)
            .Split([' ', '-', '.', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !GenericWords.Contains(w))
            .ToList();

    private static string Compact(string text) => new(CatalogText.Key(text).Where(char.IsLetterOrDigit).ToArray());
}
