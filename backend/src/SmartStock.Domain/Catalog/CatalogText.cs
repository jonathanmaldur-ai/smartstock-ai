using System.Globalization;
using System.Text;

namespace SmartStock.Domain.Catalog;

/// <summary>Limpeza de textos vindos das planilhas do ERP.</summary>
public static class CatalogText
{
    public const string NoCategory = "SEM CATEGORIA";

    /// <summary>
    /// Remove caracteres de controle (inclusive o "_x0002_" que o ERP grava), espaços duplicados e das pontas.
    /// Retorna null para texto vazio.
    /// </summary>
    public static string? Clean(string? value)
    {
        if (value is null)
            return null;

        var withoutControl = new string(value.Replace("_x0002_", string.Empty).Where(c => !char.IsControl(c)).ToArray());
        var collapsed = string.Join(' ', withoutControl.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? null : collapsed;
    }

    /// <summary>Chave de comparação: maiúsculas e sem acentos ("Alimentício" e "ALIMENTICIO" são iguais).</summary>
    public static string Key(string value)
    {
        var normalized = (Clean(value) ?? string.Empty).ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
