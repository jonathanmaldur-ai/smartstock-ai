using System.Net;
using System.Net.Http.Json;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Relatório mensal (Fase 4.2): análise atual × anterior e desempenho das transferências.</summary>
[Collection(ApiCollection.Name)]
public sealed class MonthlyReportTests(SmartStockApiFactory factory)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Compara_a_analise_atual_com_a_anterior_e_conta_a_ruptura_da_loja()
    {
        using var admin = await factory.CreateAdminClientAsync();
        await PrepareRuptureAsync(admin);
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode(); // segunda análise: há o que comparar

        var report = (await admin.GetFromJsonAsync<MonthlyDto>("/api/reports/mensal", ApiClientExtensions.Json))!;

        Assert.NotNull(report.Previous);
        Assert.NotEqual(report.Current.Id, report.Previous!.Id);
        Assert.Contains(report.Options, o => o.Id == report.Previous.Id);
        Assert.Equal(8, report.Indicators.Count);
        Assert.All(report.Indicators, i => Assert.NotNull(i.Previous));
        var mogiMirim = Assert.Single(report.Stores, s => s.Code == "06");
        Assert.True(mogiMirim.Current.RelevantRuptures >= 1);
    }

    [Fact]
    public async Task Comparar_com_uma_analise_escolhida_usa_ela()
    {
        using var admin = await factory.CreateAdminClientAsync();
        await PrepareRuptureAsync(admin);
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();
        var first = (await admin.GetFromJsonAsync<MonthlyDto>("/api/reports/mensal", ApiClientExtensions.Json))!;
        var chosen = first.Options[^1];

        var report = (await admin.GetFromJsonAsync<MonthlyDto>($"/api/reports/mensal?comparar={chosen.Id}", ApiClientExtensions.Json))!;

        Assert.Equal(chosen.Id, report.Previous!.Id);
    }

    [Fact]
    public async Task Aprovacao_entra_no_desempenho_das_transferencias()
    {
        using var admin = await factory.CreateAdminClientAsync();
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode(); // a "anterior" fica antes da decisão
        var product = await PrepareRuptureAsync(admin);
        var pending = await admin.GetFromJsonAsync<PagedDto<IdDto>>($"/api/transfer-suggestions?status=Suggested&search={product}", ApiClientExtensions.Json);
        (await admin.PostAsJsonAsync("/api/transfer-suggestions/decision", new { ids = new[] { pending!.Items.Single().Id }, approve = true })).EnsureSuccessStatusCode();

        var report = (await admin.GetFromJsonAsync<MonthlyDto>("/api/reports/mensal", ApiClientExtensions.Json))!;

        Assert.True(report.Transfers.Approved >= 1);
        Assert.NotNull(report.Transfers.PrecisionPercent);
    }

    [Fact]
    public async Task Excel_tem_as_tres_abas_e_consulta_pode_baixar()
    {
        using var admin = await factory.CreateAdminClientAsync();
        await PrepareRuptureAsync(admin);
        using var consulta = await factory.CreateClientForRoleAsync(Roles.Consulta);

        var excel = await consulta.GetAsync("/api/reports/mensal/excel");

        excel.EnsureSuccessStatusCode();
        using var workbook = new ClosedXML.Excel.XLWorkbook(await excel.Content.ReadAsStreamAsync());
        Assert.Equal(["Resumo", "Por loja", "Transferências"], workbook.Worksheets.Select(w => w.Name));
        Assert.Equal("Rupturas que importam", workbook.Worksheet("Resumo").Cell(5, 1).GetString());
    }

    [Fact]
    public async Task Analise_para_comparar_inexistente_da_404()
    {
        using var admin = await factory.CreateAdminClientAsync();
        await PrepareRuptureAsync(admin);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/reports/mensal?comparar={Guid.NewGuid()}")).StatusCode);
    }

    /// <summary>Mogi Mirim sem estoque vendendo 365/ano; Depósito com 100 un.; análise gerada.</summary>
    private static async Task<string> PrepareRuptureAsync(HttpClient admin)
    {
        var brand = UniqueCode("2");
        var product = UniqueCode("795");
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx",
            Xlsx(ProductHeaders, [product, $"PRODUTO {product}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R", ""]));
        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx", Xlsx(["Codigo do Produto", "05 DEPOSITO", "06 MOGI MIRIM"], [product, "100", "0"]), Today);
        await admin.ImportAndConfirmAsync("Sales", "vendas.xlsx", XlsxSheet("06 MOGI MIRIM", ["ivpro", "prodes", "valor", "qtde"], [product, "P", "10", "365"]), Today);
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();
        return product;
    }

    private sealed record MonthlyDto(RefDto Current, RefDto? Previous, List<RefDto> Options, List<IndicatorDto> Indicators, List<StoreDto> Stores, TransfersDto Transfers);

    private sealed record RefDto(Guid Id, DateOnly StockDate);

    private sealed record IndicatorDto(string Name, decimal Current, decimal? Previous);

    private sealed record StoreDto(string Code, MetricsDto Current, MetricsDto? Previous);

    private sealed record MetricsDto(int RelevantRuptures, int NegativeItems);

    private sealed record TransfersDto(int Approved, int Completed, decimal? PrecisionPercent);

    private sealed record IdDto(long Id);
}
