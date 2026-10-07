namespace SmartStock.Infrastructure.Imports;

internal static class CodeNormalizer
{
    private const int BrandCodeLength = 6;

    /// <summary>
    /// Códigos de marca têm 6 dígitos ("001900"). Se o Excel converteu para número e perdeu os zeros ("1900"),
    /// eles são recolocados.
    /// </summary>
    public static string? Brand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.All(char.IsDigit) && trimmed.Length < BrandCodeLength
            ? trimmed.PadLeft(BrandCodeLength, '0')
            : trimmed;
    }
}
