using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Central de relatórios (decisão 42).</summary>
[Collection(ApiCollection.Name)]
public sealed class ReportTests(SmartStockApiFactory factory)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Catalogo_lista_os_relatorios_e_as_marcas()
    {
        using var consulta = await factory.CreateClientForRoleAsync(Roles.Consulta);

        var catalog = (await consulta.GetFromJsonAsync<CatalogDto>("/api/reports", ApiClientExtensions.Json))!;

        Assert.Contains(catalog.Reports, r => r.Key == "ruptura");
        Assert.Contains(catalog.Reports, r => r.Key == "transferencias-decisoes");
        Assert.Equal(9, catalog.Reports.Count);
    }

    [Fact]
    public async Task Ruptura_filtrada_pela_marca_traz_o_produto_e_o_resumo_do_filtro()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (product, brandId) = await PrepareRuptureAsync(admin);

        var report = (await admin.GetFromJsonAsync<ReportDto>($"/api/reports/ruptura?brandId={brandId}", ApiClientExtensions.Json))!;

        Assert.Equal(1, report.TotalRows);
        Assert.Equal(product, report.Rows[0][1].GetString());
        Assert.StartsWith("Marca ", report.FilterSummary);
    }

    [Fact]
    public async Task Cobertura_e_giro_por_marca_agrega_estoque_e_vendas()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (_, brandId) = await PrepareRuptureAsync(admin);

        var report = (await admin.GetFromJsonAsync<ReportDto>($"/api/reports/por-marca?brandId={brandId}", ApiClientExtensions.Json))!;
        var row = Assert.Single(report.Rows);

        Assert.Equal(100m, row[2].GetDecimal()); // estoque: Depósito
        Assert.Equal(365m, row[3].GetDecimal()); // vendas: Mogi Mirim
        Assert.Equal(1, row[7].GetInt32()); // uma ruptura que importa
    }

    [Fact]
    public async Task Decisoes_aparecem_no_historico_e_excel_e_csv_sao_gerados()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (product, brandId) = await PrepareRuptureAsync(admin);
        var pending = await admin.GetFromJsonAsync<PagedDto<IdDto>>($"/api/transfer-suggestions?status=Suggested&search={product}", ApiClientExtensions.Json);
        (await admin.PostAsJsonAsync("/api/transfer-suggestions/decision", new { ids = new[] { pending!.Items.Single().Id }, approve = true })).EnsureSuccessStatusCode();

        var history = (await admin.GetFromJsonAsync<ReportDto>($"/api/reports/transferencias-decisoes?brandId={brandId}", ApiClientExtensions.Json))!;
        var excel = await admin.GetAsync($"/api/reports/transferencias-decisoes/excel?brandId={brandId}");
        var csv = await admin.GetAsync($"/api/reports/ruptura/csv?brandId={brandId}");

        Assert.Equal("Aprovada", history.Rows.Single()[0].GetString());
        excel.EnsureSuccessStatusCode();
        using var workbook = new ClosedXML.Excel.XLWorkbook(await excel.Content.ReadAsStreamAsync());
        Assert.Equal("Aprovada", workbook.Worksheet(1).Cell(5, 1).GetString());
        var csvText = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        Assert.Contains($";{product};", csvText);
    }

    [Fact]
    public async Task Relatorio_inexistente_da_404()
    {
        using var admin = await factory.CreateAdminClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/reports/nao-existe")).StatusCode);
    }

    /// <summary>Produto de marca nova: Mogi Mirim sem estoque vendendo 365/ano; Depósito com 100 un.; análise gerada.</summary>
    private static async Task<(string Product, int BrandId)> PrepareRuptureAsync(HttpClient admin)
    {
        var brand = UniqueCode("2");
        var product = UniqueCode("794");
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx",
            Xlsx(ProductHeaders, [product, $"PRODUTO {product}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R", ""]));
        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx", Xlsx(["Codigo do Produto", "05 DEPOSITO", "06 MOGI MIRIM"], [product, "100", "0"]), Today);
        await admin.ImportAndConfirmAsync("Sales", "vendas.xlsx", XlsxSheet("06 MOGI MIRIM", ["ivpro", "prodes", "valor", "qtde"], [product, "P", "10", "365"]), Today);
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();

        var brands = (await admin.GetFromJsonAsync<PagedDto<BrandItem>>($"/api/brands?search={brand}", ApiClientExtensions.Json))!;
        return (product, brands.Items.Single(b => b.Code == brand).Id);
    }

    private sealed record CatalogDto(List<ReportInfoDto> Reports, List<OptionDto> Brands);

    private sealed record ReportInfoDto(string Key, string Title);

    private sealed record OptionDto(int Id, string Name);

    private sealed record ReportDto(string FilterSummary, int TotalRows, List<List<JsonElement>> Rows);

    private sealed record IdDto(long Id);
}
