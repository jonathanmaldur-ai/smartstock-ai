namespace SmartStock.Domain.Catalog;

/// <summary>
/// Época de venda (decisão 65): Natal, Páscoa, Copa do Mundo... Editável pelo Administrador. As palavras-chave
/// apontam os produtos que parecem ser da época (categoria, subcategoria ou descrição).
/// </summary>
public class Season
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Primeiro mês da época (1 a 12).</summary>
    public int StartMonth { get; set; }

    /// <summary>Último mês da época (1 a 12); menor que o primeiro quando atravessa o ano (ex.: dezembro a março).</summary>
    public int EndMonth { get; set; }

    /// <summary>Só vale em ano de Copa do Mundo (2026, 2030...).</summary>
    public bool WorldCupOnly { get; set; }

    /// <summary>Palavras-chave separadas por ";", já normalizadas (<see cref="CatalogText.Key"/>).</summary>
    public string Keywords { get; set; } = string.Empty;

    /// <summary>Procura as palavras-chave também na descrição (desligado em Volta às aulas: "ônibus escolar" é brinquedo).</summary>
    public bool MatchDescription { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedByEmail { get; set; }

    public IReadOnlyList<string> KeywordList =>
        Keywords.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A data está dentro da época (mês dentro do intervalo e, se for o caso, ano de Copa).</summary>
    public bool IsInSeason(DateOnly date)
    {
        if (WorldCupOnly && !IsWorldCupYear(date.Year))
            return false;
        return StartMonth <= EndMonth
            ? date.Month >= StartMonth && date.Month <= EndMonth
            : date.Month >= StartMonth || date.Month <= EndMonth;
    }

    /// <summary>A Copa do Mundo masculina acontece de 4 em 4 anos desde 2022 (2026, 2030...).</summary>
    public static bool IsWorldCupYear(int year) => year % 4 == 2;

    /// <summary>Normaliza a lista digitada: maiúsculas, sem acentos, sem repetição.</summary>
    public static string NormalizeKeywords(IEnumerable<string> keywords) =>
        string.Join(';', keywords.Select(CatalogText.Key).Where(k => k.Length > 0).Distinct());
}

public enum SeasonalSuggestionKind
{
    /// <summary>A categoria, a subcategoria ou a descrição tem uma palavra-chave da época.</summary>
    Keyword = 1,

    /// <summary>Vendia bem em 12 meses e quase não vendeu nos últimos 60 dias.</summary>
    SalesStopped = 2
}

public enum SeasonalSuggestionStatus
{
    Pending = 1,
    Confirmed = 2,
    Rejected = 3
}

/// <summary>
/// Sugestão de que um produto é sazonal (decisão 65). O sistema só sugere; quem confirma é o Administrador
/// (a IA nunca altera dados sozinha: CLAUDE.md).
/// </summary>
public class SeasonalSuggestion
{
    public long Id { get; set; }
    public int ProductId { get; set; }

    /// <summary>Época sugerida; null quando só o comportamento de venda aponta (vira "fora de época" ao confirmar).</summary>
    public int? SeasonId { get; set; }
    public Season? Season { get; set; }

    public SeasonalSuggestionKind Kind { get; set; }
    public SeasonalSuggestionStatus Status { get; set; } = SeasonalSuggestionStatus.Pending;

    /// <summary>Por que foi sugerido, em texto (ex.: "Categoria NATAL").</summary>
    public string Evidence { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedByEmail { get; set; }
}
