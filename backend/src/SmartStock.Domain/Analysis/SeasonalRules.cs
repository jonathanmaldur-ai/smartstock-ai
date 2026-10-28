using SmartStock.Domain.Catalog;
using SmartStock.Domain.Inventory;

namespace SmartStock.Domain.Analysis;

/// <summary>Em que momento da época o produto está, na data da análise (decisão 65).</summary>
public enum SeasonState
{
    /// <summary>Produto comum, sem época.</summary>
    None,

    /// <summary>Época confirmada e dentro do período: a venda recente pesa mais que a média de 12 meses.</summary>
    InSeason,

    /// <summary>Fora da época (ou marcado à mão como fora de época): sem sugestões e sem alarme de ruptura.</summary>
    OutOfSeason
}

/// <summary>Regras de sazonalidade (decisão 65): detecção, estado na data e efeito nas métricas.</summary>
public static class SeasonalRules
{
    /// <summary>Venda mínima em 12 meses (rede) para o "parou de vender" ter significado.</summary>
    public const decimal MinimumSoldToDetect = 50;

    /// <summary>Janela da venda recente usada para ver se o produto parou de vender.</summary>
    public const int StoppedWindowDays = 60;

    /// <summary>Vendeu até esta fração dos 12 meses na janela: parou (o normal seria ~16% em 60 dias).</summary>
    public const decimal StoppedShare = 0.02m;

    /// <summary>Janela da venda recente usada no pico da época.</summary>
    public const int PeakWindowDays = 30;

    /// <summary>Fração mínima de dias com venda importada para a loja entrar na conta (evita buraco, ex.: dias não importados).</summary>
    public const decimal MinimumDaysCoverage = 0.8m;

    public static SeasonState StateOf(Season? season, bool markedOutOfSeason, DateOnly date) =>
        markedOutOfSeason ? SeasonState.OutOfSeason
        : season is null || !season.IsActive ? SeasonState.None
        : season.IsInSeason(date) ? SeasonState.InSeason
        : SeasonState.OutOfSeason;

    /// <summary>
    /// A primeira época ativa com palavra-chave na categoria, subcategoria ou descrição (palavra inteira); a descrição só
    /// quando a época permite.
    /// </summary>
    public static (Season Season, string Evidence)? MatchKeyword(IEnumerable<Season> seasons, string category, string? subcategory, string description)
    {
        // Cada campo é limpo uma vez só: a análise passa por dezenas de milhares de produtos.
        var fields = new (string Label, string Text, string Words)[]
        {
            Field("Categoria", category), Field("Subcategoria", subcategory ?? string.Empty), Field("Descrição", description)
        };
        foreach (var season in seasons.Where(s => s.IsActive).OrderBy(s => s.Id))
        foreach (var keyword in season.KeywordList)
        foreach (var (label, text, words) in season.MatchDescription ? fields : fields[..2])
        {
            if (keyword.Length > 0 && words.Contains($" {keyword} ", StringComparison.Ordinal))
                return (season, label == "Descrição" ? $"Descrição com \"{keyword}\"" : $"{label} {text}");
        }
        return null;
    }

    /// <summary>Vendia bem nos 12 meses e quase nada na janela recente.</summary>
    public static bool HasStopped(decimal sold12Months, decimal soldRecently) =>
        sold12Months >= MinimumSoldToDetect && soldRecently <= sold12Months * StoppedShare;

    /// <summary>Na época, a venda considerada é a maior entre os 12 meses e os últimos 30 dias anualizados.</summary>
    public static decimal PeakSold(decimal sold12Months, decimal soldLast30Days) =>
        Math.Max(sold12Months, soldLast30Days * StockMetrics.SalesWindowDays / PeakWindowDays);

    /// <summary>
    /// Fora da época, sobra de estoque não é "parado" nem "excesso" comuns e falta não é ruptura:
    /// vira "Sobra de época" (ou "Sem movimento", sem estoque), sem prioridade.
    /// </summary>
    public static PositionMetrics AsOutOfSeason(PositionMetrics metrics) =>
        metrics.Situation == StockSituation.Warehouse
            ? metrics
            : metrics with
            {
                Situation = metrics.ProjectedStock > 0 ? StockSituation.OutOfSeason : StockSituation.NoMovement,
                Priority = AlertPriority.None
            };

    /// <summary>Texto normalizado e a versão com espaços nas pontas e pontuação como espaço ("NATAL/ENFEITE" acha "NATAL").</summary>
    private static (string Label, string Text, string Words) Field(string label, string value)
    {
        var text = CatalogText.Key(value);
        var words = string.Join(' ', new string(text.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return (label, text, $" {words} ");
    }
}
