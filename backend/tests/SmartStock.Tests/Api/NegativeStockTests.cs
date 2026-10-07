using System.Net.Http.Json;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Painel de estoque negativo (decisões 26 e 38).</summary>
[Collection(ApiCollection.Name)]
public sealed class NegativeStockTests(SmartStockApiFactory factory)
{
    private static readonly string[] StockHeaders = ["Codigo do Produto", "01 MATRIZ", "05 DEPOSITO", "06 MOGI MIRIM"];
    private static readonly string[] SalesHeaders = ["ivpro", "prodes", "valor", "qtde"];
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Negativo_em_produto_que_vende_e_critico_com_causa_provavel_e_entra_no_resumo()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareAsync(admin, mogiMirimStock: "-15", depositoStock: "-40");

        var item = (await ListAsync(admin, $"search={product}")).Single(i => i.StoreCode == "06");
        var deposito = (await ListAsync(admin, $"search={product}")).Single(i => i.StoreCode == "05");
        var summary = (await admin.GetFromJsonAsync<SummaryDto>("/api/analysis/negatives/summary", ApiClientExtensions.Json))!;

        Assert.Equal(("Critical", -15m), (item.Priority, item.Quantity));
        Assert.Contains("SaleWithoutEntry", item.Causes);
        Assert.Equal("High", deposito.Priority);
        Assert.True(summary.Items >= 2);
        Assert.Contains(summary.Stores, s => s.Code == "06" && s.Items >= 1 && s.PercentOfItems > 0);
    }

    [Fact]
    public async Task Filtro_por_causa_e_excel_para_correcao_no_erp()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareAsync(admin, mogiMirimStock: "-2.5", depositoStock: "10");

        var fractional = await ListAsync(admin, $"search={product}&cause=FractionalUnit");
        var export = await admin.GetAsync($"/api/analysis/negatives/export?search={product}");

        Assert.Single(fractional);
        export.EnsureSuccessStatusCode();
        using var workbook = new ClosedXML.Excel.XLWorkbook(await export.Content.ReadAsStreamAsync());
        Assert.Equal(product, workbook.Worksheet(1).Cell(2, 3).GetString());
        Assert.Contains("Unidade fracionada", workbook.Worksheet(1).Cell(2, 10).GetString());
    }

    [Fact]
    public async Task Ranking_por_marca_lista_as_maiores_somas_negativas()
    {
        using var admin = await factory.CreateAdminClientAsync();
        await PrepareAsync(admin, mogiMirimStock: "-5000", depositoStock: "0");

        var ranking = (await admin.GetFromJsonAsync<List<GroupDto>>("/api/analysis/negatives/ranking?by=Brand", ApiClientExtensions.Json))!;

        Assert.NotEmpty(ranking);
        Assert.True(ranking[0].Units <= -5000);
    }

    /// <summary>Produto novo com estoque nas lojas informadas, vendendo 365/ano em Mogi Mirim, e análise gerada.</summary>
    private static async Task<string> PrepareAsync(HttpClient admin, string mogiMirimStock, string depositoStock)
    {
        var brand = UniqueCode("5");
        var product = UniqueCode("791");
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx",
            Xlsx(ProductHeaders, [product, $"PRODUTO {product}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R", ""]));
        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx", Xlsx(StockHeaders, [product, "0", depositoStock, mogiMirimStock]), Today);
        await admin.ImportAndConfirmAsync("Sales", "vendas.xlsx", XlsxSheet("06 MOGI MIRIM", SalesHeaders, [product, "P", "10", "365"]), Today);
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();
        return product;
    }

    private static async Task<List<ItemDto>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PagedDto<ItemDto>>($"/api/analysis/negatives?{query}&pageSize=100", ApiClientExtensions.Json))!.Items;

    private sealed record ItemDto(string StoreCode, decimal Quantity, string Priority, List<string> Causes);

    private sealed record StoreDto(string Code, int Items, decimal PercentOfItems);

    private sealed record SummaryDto(int Items, List<StoreDto> Stores);

    private sealed record GroupDto(string Name, int Items, decimal Units);
}
