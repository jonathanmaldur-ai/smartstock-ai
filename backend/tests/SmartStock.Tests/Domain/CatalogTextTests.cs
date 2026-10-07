using SmartStock.Domain.Catalog;

namespace SmartStock.Tests.Domain;

public sealed class CatalogTextTests
{
    [Theory]
    [InlineData("BRINQUEDOS_x0002_", "BRINQUEDOS")]
    [InlineData("  CARTAS   DE BARAL ", "CARTAS DE BARAL")]
    [InlineData("\u0002", null)]
    [InlineData("", null)]
    public void Limpa_textos_do_ERP(string input, string? expected) => Assert.Equal(expected, CatalogText.Clean(input));

    [Fact]
    public void Chave_ignora_acentos_e_maiusculas() =>
        Assert.Equal(CatalogText.Key("Alimentício"), CatalogText.Key("ALIMENTICIO"));

    [Theory]
    [InlineData("6", "06")]
    [InlineData("06 MOGI MIRIM", "06")]
    [InlineData("18", "18")]
    [InlineData("MATRIZ", null)]
    public void Codigo_de_loja_e_normalizado(string input, string? expected) =>
        Assert.Equal(expected, Store.NormalizeCode(input));
}
