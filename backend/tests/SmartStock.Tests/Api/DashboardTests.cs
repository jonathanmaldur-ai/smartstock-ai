using System.Net.Http.Json;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Dashboard executivo (decisão 39).</summary>
[Collection(ApiCollection.Name)]
public sealed class DashboardTests(SmartStockApiFactory factory)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Dashboard_traz_kpis_lojas_top_produtos_tendencia_e_idade_dos_dados()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var brand = UniqueCode("4");
        var product = UniqueCode("792");
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx",
            Xlsx(ProductHeaders, [product, $"PRODUTO {product}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R", ""]));
        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx",
            Xlsx(["Codigo do Produto", "05 DEPOSITO", "06 MOGI MIRIM"], [product, "1000", "0"]), Today);
        await admin.ImportAndConfirmAsync("Sales", "vendas.xlsx",
            XlsxSheet("06 MOGI MIRIM", ["ivpro", "prodes", "valor", "qtde"], [product, "P", "10", "9999999"]), Today);
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();

        using var consulta = await factory.CreateClientForRoleAsync(Roles.Consulta);
        var dashboard = (await consulta.GetFromJsonAsync<DashboardDto>("/api/dashboard", ApiClientExtensions.Json))!;

        Assert.NotNull(dashboard.Kpis);
        Assert.Equal(Today, dashboard.Freshness.StockDate);
        Assert.False(dashboard.Freshness.StockOutdated);
        Assert.True(dashboard.Kpis!.RelevantRuptures >= 1);
        Assert.Equal(product, dashboard.TopProducts[0].Code); // vende 9.999.999: o mais vendido da base de testes
        Assert.Equal(1, dashboard.TopProducts[0].RuptureStores);
        Assert.Contains(dashboard.Stores, s => s.Code == "06" && s.Rupture >= 1);
        Assert.NotEmpty(dashboard.Trend);
    }

    private sealed record FreshnessDto(DateOnly? StockDate, bool StockOutdated);

    private sealed record KpisDto(int RelevantRuptures, decimal StockUnits);

    private sealed record StoreDto(string Code, int Rupture);

    private sealed record TopDto(string Code, int RuptureStores);

    private sealed record TrendDto(DateOnly AnalysisDate);

    private sealed record DashboardDto(FreshnessDto Freshness, KpisDto? Kpis, List<StoreDto> Stores, List<TopDto> TopProducts, List<TrendDto> Trend);
}
