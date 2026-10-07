using System.Net.Http.Json;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Vendas por dia (decisão 51): importação do relatório de vendas do ERP e o relatório da central.</summary>
[Collection(ApiCollection.Name)]
public sealed class DailySalesTests(SmartStockApiFactory factory)
{
    private static readonly string[] Headers =
        ["vennum", "vendta", "venhor", "vencli", "fcnom", "ventot", "vended", "vdnom", "qtdtot", "ventip", "mvdes", "venbru", "vendct"];

    [Fact]
    public async Task Importa_o_total_de_cada_dia_sem_dados_de_cliente_e_reimportar_substitui_os_dias()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (d1, d2) = Days();
        var file = Xlsx(Headers,
            Sale("1", d1, "100.00", "2", "110.00"),
            Sale("2", d1, "50.00", "1", "50.00"),
            Sale("2", d1, "50.00", "1", "50.00"),
            Sale("3", d2, "30.00", "3", "30.00"));

        var report = await admin.ImportAndConfirmAsync("DailySales", "MOGI MIRIM.XLS", file);

        Assert.Equal(("06", 3), (report.StoreCode, report.ValidRows));
        Assert.Contains(report.IssueGroups, g => g.Code == "daily.duplicated");
        var sales = await GetAsync(admin, d1, d2);
        Assert.Equal((180m, 3, 6m), (sales.Current.Net, sales.Current.Sales, sales.Current.Pieces));
        Assert.Equal(60m, sales.Current.AverageTicket);
        var store = Assert.Single(sales.Stores);
        Assert.Equal(("06", d1, 150m), (store.Code, store.BestDay, store.BestDayNet));

        await admin.ImportAndConfirmAsync("DailySales", "MOGI MIRIM.XLS", Xlsx(Headers, Sale("9", d2, "70.00", "1", "70.00")));

        var replaced = await GetAsync(admin, d1, d2);
        Assert.Equal((220m, 3), (replaced.Current.Net, replaced.Current.Sales)); // d1 continua; d2 trocado
    }

    [Fact]
    public async Task Compara_com_o_periodo_anterior_e_mostra_o_dia_da_semana()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (d1, d2) = Days();
        var before1 = d1.AddDays(-2);
        await admin.ImportAndConfirmAsync("DailySales", "TAUBATE.XLS",
            Xlsx(Headers, Sale("1", before1, "40.00", "1", "40.00"), Sale("2", d1, "100.00", "1", "100.00"), Sale("3", d2, "300.00", "2", "300.00")));
        var storeId = await StoreIdAsync(admin, "11");

        var sales = await GetAsync(admin, d1, d2, storeId);

        Assert.Equal("11 Taubaté", sales.Scope);
        Assert.Equal(400m, sales.Current.Net);
        Assert.Equal(40m, sales.Previous!.Net);
        Assert.Equal(d1.AddDays(-2), sales.PreviousFrom);
        Assert.Equal(300m, sales.Weekdays[(int)d2.DayOfWeek].AverageNet);
        var excel = await admin.GetAsync($"/api/reports/vendas-diarias/excel?de={d1:yyyy-MM-dd}&ate={d2:yyyy-MM-dd}&storeId={storeId}");
        excel.EnsureSuccessStatusCode();
        using var workbook = new ClosedXML.Excel.XLWorkbook(await excel.Content.ReadAsStreamAsync());
        Assert.Equal(["Resumo", "Por dia", "Por loja", "Dia da semana"], workbook.Worksheets.Select(w => w.Name));
    }

    [Fact]
    public async Task Relatorio_detalhado_por_itens_grava_o_dia_e_os_produtos_vendidos()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, b) = await CreateProductsAsync(admin);
        var (d1, _) = Days();
        var date = d1.ToString("dd-MMM-yy", System.Globalization.CultureInfo.InvariantCulture);
        var day = d1.ToString("dd/MM/yyyy");
        string[] headers = ["texto", "qtde", "vuni", "vtot", "vendta", "vennum", "tiporeg", "prouni", "qtdemask"];
        var file = Xlsx(headers,
            [$"0154604201  {day}  CLIENTE BALCAO  [Vr. Bruto: R$40,00  A/D: 0,00 %]", "2", "0", "35.97", date, "0154604201", "1", "", "2,0"],
            [" Vd: [000200]-USUARIO9  F.Pagto: [000100]-01-A VISTA  Doc. fiscal: [NFC-e]", "0", "0", "0", date, "0154604201", "1", "", ""],
            [$"   {a} PRODUTO A", "2", "12.99", "25.98", date, "0154604201", "2", "PC", "2,0000"],
            [$"   {b} PRODUTO B", "1", "9.99", "9.99", date, "0154604201", "2", "UN", "1,000"],
            [$"0154604301  {day}  CLIENTE BALCAO  [Vr. Bruto: R$20,00  A/D: 0,00 %]", "1", "0", "20.00", date, "0154604301", "1", "", "1,0"],
            [" Vd: [000100]-VENDEDOR LOJA  F.Pagto: [000100]-01-A VISTA  Doc. fiscal: [NFC-e]", "0", "0", "0", date, "0154604301", "1", "", ""],
            [$"   {a} PRODUTO A", "1", "20", "20.00", date, "0154604301", "2", "PC", "1,0000"],
            ["   9999999999999 FORA DO CADASTRO", "1", "1", "1.00", date, "0154604301", "2", "PC", "1,0000"]);

        var report = await admin.ImportAndConfirmAsync("DailySales", "PINDA.XLS", file);

        Assert.Equal("18", report.StoreCode); // nome abreviado
        Assert.Contains(report.IssueGroups, g => g.Code == "daily.product_not_found");
        var sales = await GetAsync(admin, d1, d1, await StoreIdAsync(admin, "18"));
        Assert.Equal((55.97m, 60m, 2, 5m), (sales.Current.Net, sales.Current.Gross, sales.Current.Sales, sales.Current.Pieces));
        Assert.True(sales.Products.Available);
        var top = sales.Products.Items[0];
        Assert.Equal((a, 3m, 45.98m), (top.Code, top.Quantity, top.Amount));
        Assert.Equal(2, sales.Products.Total);

        var storeId = await StoreIdAsync(admin, "18");
        var byDay = (await admin.GetFromJsonAsync<PagedDto<ProductDayDto>>(
            $"/api/reports/vendas-diarias/produtos?de={d1:yyyy-MM-dd}&ate={d1:yyyy-MM-dd}&storeId={storeId}", ApiClientExtensions.Json))!;
        Assert.Equal(2, byDay.TotalCount);
        Assert.Equal((d1, "18", a, 3m, 45.98m), (byDay.Items[0].Date, byDay.Items[0].StoreCode, byDay.Items[0].Code, byDay.Items[0].Quantity, byDay.Items[0].Amount));
        var searched = (await admin.GetFromJsonAsync<PagedDto<ProductDayDto>>(
            $"/api/reports/vendas-diarias/produtos?de={d1:yyyy-MM-dd}&ate={d1:yyyy-MM-dd}&storeId={storeId}&search={b}", ApiClientExtensions.Json))!;
        Assert.Equal(b, Assert.Single(searched.Items).Code);
        Assert.Equal(2, byDay.Items[0].Sales);

        var productId = (await admin.GetFromJsonAsync<PagedDto<ProductItem>>($"/api/products?search={a}", ApiClientExtensions.Json))!.Items.Single().Id;
        var lines = (await admin.GetFromJsonAsync<List<SaleLineDto>>(
            $"/api/reports/vendas-diarias/produtos/vendas?storeId={storeId}&de={d1:yyyy-MM-dd}&ate={d1:yyyy-MM-dd}&productId={productId}", ApiClientExtensions.Json))!;
        Assert.Equal(2, lines.Count);
        Assert.Equal(("0154604201", 2m, 12.99m, 35.97m, 2, false), (lines[0].SaleNumber, lines[0].Quantity, lines[0].UnitPrice, lines[0].SaleTotal, lines[0].SaleItems, lines[0].RegisteredCustomer));
        Assert.Equal(("01-A VISTA", "NFC-e"), (lines[0].Payment, lines[0].FiscalDocument));
    }

    private sealed record SaleLineDto(
        string SaleNumber, decimal Quantity, decimal UnitPrice, decimal Amount, decimal SaleTotal, int SaleItems, bool RegisteredCustomer,
        string? Payment, string? FiscalDocument);

    private sealed record ProductDayDto(DateOnly Date, string StoreCode, string Code, decimal Quantity, decimal Amount, int Sales);

    /// <summary>Cria uma marca e dois produtos novos (códigos únicos) e devolve os códigos.</summary>
    private static async Task<(string A, string B)> CreateProductsAsync(HttpClient admin)
    {
        var brand = UniqueCode("7");
        var (a, b) = (UniqueCode("781"), UniqueCode("781"));
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx", Xlsx(ProductHeaders,
            [a, $"PRODUTO A {a}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R1", ""],
            [b, $"PRODUTO B {b}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R2", ""]));
        return (a, b);
    }

    /// <summary>Dois dias seguidos num ano só deste teste, para não somar com outros testes no mesmo banco.</summary>
    private static (DateOnly, DateOnly) Days()
    {
        var day = new DateOnly(Random.Shared.Next(1990, 2020), Random.Shared.Next(1, 12), Random.Shared.Next(3, 27));
        return (day, day.AddDays(1));
    }

    private static string[] Sale(string number, DateOnly date, string net, string pieces, string gross) =>
        [$"{number}{date:yyyyMMdd}", date.ToString("dd-MMM-yy", System.Globalization.CultureInfo.InvariantCulture), "10:00:00",
         "123", "CLIENTE TESTE", net, "1", "VENDEDOR", pieces, "000100", "01-A VISTA", gross, "0"];

    private static async Task<SalesDto> GetAsync(HttpClient client, DateOnly from, DateOnly to, int? storeId = null) =>
        (await client.GetFromJsonAsync<SalesDto>(
            $"/api/reports/vendas-diarias?de={from:yyyy-MM-dd}&ate={to:yyyy-MM-dd}{(storeId is null ? "" : $"&storeId={storeId}")}",
            ApiClientExtensions.Json))!;

    private static async Task<int> StoreIdAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<List<StoreDto>>("/api/stores", ApiClientExtensions.Json))!.Single(s => s.Code == code).Id;

    private sealed record SalesDto(
        string Scope, DateOnly? PreviousFrom, TotalsDto Current, TotalsDto? Previous, List<StoreRowDto> Stores, List<WeekdayDto> Weekdays,
        ProductsDto Products);

    private sealed record ProductsDto(bool Available, int Total, List<ProductDto> Items);

    private sealed record ProductDto(string Code, decimal Quantity, decimal Amount);

    private sealed record TotalsDto(decimal Net, decimal Gross, int Sales, decimal Pieces, decimal? AverageTicket);

    private sealed record StoreRowDto(string Code, DateOnly? BestDay, decimal? BestDayNet);

    private sealed record WeekdayDto(int DayOfWeek, decimal AverageNet);

    private sealed record StoreDto(int Id, string Code);
}
