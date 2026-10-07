using System.Globalization;

namespace SmartStock.Infrastructure.Imports;

/// <summary>
/// Números das planilhas do ERP. Células numéricas chegam no formato invariante ("264529.4805", "-6");
/// alguns relatórios gravam a quantidade como texto em português ("24.372,0000"). Texto com vírgula é lido
/// como português (ponto = milhar); sem vírgula, como invariante.
/// </summary>
internal static class NumberParser
{
    private static readonly CultureInfo PortugueseBrazil = CultureInfo.GetCultureInfo("pt-BR");
    private const NumberStyles Styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands;

    /// <summary>Célula vazia vale zero; texto que não é número retorna null.</summary>
    public static decimal? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var text = value.Trim();
        var culture = text.Contains(',') ? PortugueseBrazil : CultureInfo.InvariantCulture;
        var styles = culture == PortugueseBrazil ? Styles : Styles & ~NumberStyles.AllowThousands;
        return decimal.TryParse(text, styles, culture, out var number) ? number : null;
    }
}
