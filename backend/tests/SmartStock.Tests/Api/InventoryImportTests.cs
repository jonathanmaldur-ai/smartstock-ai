using System.Net;
using System.Net.Http.Json;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Módulo 2.2: estoque, vendas e transferências (decisões 31 a 34), com planilhas inventadas.</summary>
[Collection(ApiCollection.Name)]
public sealed class InventoryImportTests(SmartStockApiFactory factory)
{
    private static readonly string[] StockHeaders =
        ["Codigo do Produto", "Descrição", "Quantidade Geral", "01 MATRIZ", "05 DEPOSITO", "06 MOGI MIRIM", "14 LOJA FECHADA"];

    private static readonly string[] StoreSalesHeaders = ["ivpro", "prodes", "valor", "vrpor", "qtde", "qtdpor", "prouni", "proref", "qtdemask"];
    private static readonly string[] MatrizSalesHeaders = ["Codigo Produto", "Descrição do Produto", "Qtd Vendida", "", "%Qtd", "valor vendido", "%valor"];
    private static readonly string[] TransferHeaders = ["DESCRIÇÃO DA TRANSFERENCIA", "ENTRADA", "SAIDA", "USUARIO", "STATUS TRANSFERENCIA"];

    private static readonly string[] AlternativeTransferHeaders =
        ["mpent", "mpsai", "prouni", "grupo", "qtdent", "DESCRIÇÃO TRANSFERENCIA", "ENTRADA", "SAIDA", "", "USUARIO", "MOTIVO"];

    /// <summary>Hoje: a foto de estoque e as vendas do teste passam a ser as "atuais" (vale a data mais recente).</summary>
    private static readonly DateOnly DataDate = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Estoque_exige_a_data_dos_dados()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var file = Xlsx(StockHeaders, ["123", "X", "0", "0", "0", "0", "0"]);

        var withoutDate = await admin.UploadAsync("Stock", "Estoque.xlsx", file);
        var future = await admin.UploadAsync("Stock", "Estoque.xlsx", file, DateOnly.FromDateTime(DateTime.Today.AddDays(2)));

        Assert.Equal(HttpStatusCode.BadRequest, withoutDate.StatusCode);
        Assert.Equal("import.date_missing", await withoutDate.ProblemCodeAsync());
        Assert.Equal("import.date_in_future", await future.ProblemCodeAsync());
    }

    [Fact]
    public async Task Estoque_grava_as_lojas_ativas_e_aparece_na_ficha_do_produto()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, b) = await CreateProductsAsync(admin);
        var file = Xlsx(StockHeaders,
            [a, "PRODUTO A", "20.5", "12", "-3", "11.5", "7"],
            [b, "PRODUTO B", "0", "0", "0", "0", "0"],
            ["999999999999", "NÃO CADASTRADO", "1", "1", "0", "0", "0"],
            ["TOTAIS", "2 itens.", "", "", "", "", ""]);

        var report = await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx", file, DataDate);

        Assert.Equal("Confirmed", report.Status);
        Assert.Equal(DataDate, report.ReferenceDate);
        Assert.Equal((2, 1), (report.ValidRows, report.ErrorRows));
        Assert.Contains(report.IssueGroups, g => g.Code == "stock.product_not_found");
        Assert.Contains(report.IssueGroups, g => g.Code == "stock.store_closed" && g.SampleValues.Contains("14 LOJA FECHADA"));
        Assert.Contains(report.IssueGroups, g => g.Code == "stock.total_mismatch");

        var overview = await GetOverviewAsync(admin, a);
        Assert.Equal(DataDate, overview.StockDate);
        Assert.Equal(12m, Store(overview, "01").Stock);
        Assert.Equal(-3m, Store(overview, "05").Stock);
        Assert.Equal(11.5m, Store(overview, "06").Stock);
        Assert.Equal(0m, Store(overview, "04").Stock);
    }

    [Fact]
    public async Task Vendas_reconhecem_a_loja_pela_aba_e_calculam_vmd_e_cobertura()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, _) = await CreateProductsAsync(admin);
        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx", Xlsx(StockHeaders, [a, "A", "", "10", "0", "30", "0"]), DataDate);

        var sales = await admin.ImportAndConfirmAsync("Sales", "vendas.xlsx",
            XlsxSheet("06 MOGI MIRIM", StoreSalesHeaders, [a, "PRODUTO A", "3650.00", "1", "730", "1", "PC", "R1", "730,0000"]),
            DataDate);

        Assert.Equal(("06", 1), (sales.StoreCode, sales.ValidRows));
        Assert.Equal((DataDate.AddYears(-1), DataDate), (sales.PeriodStart!.Value, sales.PeriodEnd!.Value));

        var mogiMirim = Store(await GetOverviewAsync(admin, a), "06");
        Assert.Equal(730m, mogiMirim.Sold12Months);
        Assert.Equal(2m, mogiMirim.DailyAverage);
        Assert.Equal(15m, mogiMirim.CoverageDays);
    }

    [Fact]
    public async Task Vendas_da_matriz_leem_a_quantidade_em_texto_e_a_loja_pelo_nome_do_arquivo()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, b) = await CreateProductsAsync(admin);
        var file = XlsxSheet("Planilha1", MatrizSalesHeaders,
            [a, "PRODUTO A", "  1.234,5000", "", "1", "10.5", "1"],
            [b, "PRODUTO B", "abc", "", "1", "1", "1"]);

        var report = await admin.ImportAndConfirmAsync("Sales", "01 MATRIZ.xlsx", file, DataDate);

        Assert.Equal(("01", 1, 1), (report.StoreCode, report.ValidRows, report.ErrorRows));
        Assert.Equal(1234.5m, Store(await GetOverviewAsync(admin, a), "01").Sold12Months);
    }

    [Theory]
    [InlineData("SHOPPINH BURITI.XLS", "09")]
    [InlineData("SAO JOSE DOS CAMPOS.XLS", "10")]
    [InlineData("ECOMMERCE.XLS", "12")]
    public async Task Vendas_como_saem_do_erp_reconhecem_a_loja_pelo_nome_do_arquivo(string fileName, string storeCode)
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, _) = await CreateProductsAsync(admin);
        var file = XlsxSheet("Sheet", StoreSalesHeaders, [a, "PRODUTO A", "10", "0", "7", "0", "PC", "R", "7,000"]);

        var report = await (await admin.UploadAsync("Sales", fileName, file, DataDate)).ReadAsync<ImportReportDto>();

        Assert.Equal(("Validated", storeCode), (report.Status, report.StoreCode));
    }

    [Fact]
    public async Task Vendas_com_nome_de_loja_incompleto_sao_recusadas()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var file = XlsxSheet("Sheet", StoreSalesHeaders, ["1", "X", "1", "1", "1", "1", "PC", "R", "1"]);

        var report = await (await admin.UploadAsync("Sales", "MOGI.XLS", file, DataDate)).ReadAsync<ImportReportDto>();

        Assert.Equal("Rejected", report.Status); // "MOGI" sozinho não diz qual loja: Mogi Mirim precisa das duas palavras
    }

    [Fact]
    public async Task Vendas_sem_loja_identificada_sao_recusadas()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var file = XlsxSheet("Planilha1", StoreSalesHeaders, ["1", "X", "1", "1", "1", "1", "PC", "R", "1"]);

        var unknown = await (await admin.UploadAsync("Sales", "vendas.xlsx", file, DataDate)).ReadAsync<ImportReportDto>();
        var closed = await (await admin.UploadAsync("Sales", "vendas.xlsx", file, DataDate, storeCode: "14")).ReadAsync<ImportReportDto>();

        Assert.Equal("Rejected", unknown.Status);
        Assert.Contains("Escolha a loja", unknown.RejectionReason);
        Assert.Equal("Rejected", closed.Status);
        Assert.Contains("fechada", closed.RejectionReason);
    }

    [Fact]
    public async Task Transferencias_gravam_entradas_saidas_e_cancelamentos()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, _) = await CreateProductsAsync(admin);
        var year = UniqueYear();
        var file = Xlsx(TransferHeaders,
            [$"{a} - PRODUTO A", "0", "0", "", ""],
            [$"     10/03/{year} - TRANSFERENCIA DE 05 PARA 06", "0", "12", "USUARIO1", "TRANSFERENCIA"],
            [$"     12/03/{year} - TRANSFERENCIA DE 05 PARA 06", "12", "0", "USUARIO4", "TRANSFERENCIA"],
            [$"     13/03/{year} - CANC. TRANSF. DE 01 PARA 11", "4", "0", "USUARIO1", "TRANSFERENCIA"],
            [$"     14/03/{year} - TRANSFERENCIA DE 14 PARA 06", "5", "0", "USUARIO3", "TRANSFERENCIA"],
            [$"     15/03/{year} - TRANSFERENCIA DE 14 PARA 13", "0", "1", "USUARIO3", "TRANSFERENCIA"],
            ["TOTAL:", "21", "13", "", ""],
            ["", "0", "0", "", ""]);

        var report = await admin.ImportAndConfirmAsync("Transfers", "Transferencias.xlsx", file);

        Assert.Equal(4, report.ValidRows);
        Assert.Equal((new DateOnly(year, 3, 10), new DateOnly(year, 3, 14)), (report.PeriodStart!.Value, report.PeriodEnd!.Value));
        Assert.Contains(report.IssueGroups, g => g.Code == "transfer.stores_closed");
        Assert.DoesNotContain(report.IssueGroups, g => g.Code == "transfer.total_mismatch");

        var transfers = (await GetOverviewAsync(admin, a)).RecentTransfers;
        Assert.Equal(4, transfers.Count);
        Assert.Contains(transfers, t => t is { OriginCode: "14", DestinationCode: "06", Direction: "In", Quantity: 5 });
        Assert.Contains(transfers, t => t is { IsCancellation: true, OriginCode: "01", DestinationCode: "11", Direction: "In" });
        Assert.Contains(transfers, t => t is { Direction: "Out", Quantity: 12, UserName: "USUARIO1" });
    }

    [Fact]
    public async Task Transferencias_no_layout_alternativo_usam_as_colunas_mpent_e_mpsai()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, _) = await CreateProductsAsync(admin);
        var year = UniqueYear();
        var file = Xlsx(AlternativeTransferHeaders,
            ["0", "0", "", "", "0", $"{a} - PRODUTO A", "0", "0", "", "", ""],
            ["0", "6", "", "", "0", $"     06/01/{year} - TRANSFERENCIA DE 05 PARA 07", "0", "0", "", "USUARIO6", "TRANSFERENCIA"],
            ["6", "0", "", "", "0", $"     07/01/{year} - TRANSFERENCIA DE 05 PARA 07", "0", "0", "", "USUARIO5", "TRANSFERENCIA"],
            ["9", "6", "", "", "0", "TOTAL:", "0", "0", "", "", ""]);

        var report = await admin.ImportAndConfirmAsync("Transfers", "Transferencias.xlsx", file);

        Assert.Equal(2, report.ValidRows);
        Assert.Contains(report.IssueGroups, g => g.Code == "transfer.total_mismatch");
        var transfers = (await GetOverviewAsync(admin, a)).RecentTransfers;
        Assert.Equal(["In", "Out"], transfers.Select(t => t.Direction).Order());
    }

    [Fact]
    public async Task Reimportar_o_mesmo_periodo_substitui_as_transferencias()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, _) = await CreateProductsAsync(admin);
        var year = UniqueYear();
        byte[] File(string quantity) => Xlsx(TransferHeaders,
            [$"{a} - PRODUTO A", "0", "0", "", ""],
            [$"     01/05/{year} - TRANSFERENCIA DE 05 PARA 10", "0", quantity, "USUARIO1", "TRANSFERENCIA"],
            [$"     31/05/{year} - TRANSFERENCIA DE 05 PARA 10", quantity, "0", "USUARIO8", "TRANSFERENCIA"],
            ["TOTAL:", quantity, quantity, "", ""]);

        await admin.ImportAndConfirmAsync("Transfers", "maio.xlsx", File("10"));
        var second = await admin.ImportAndConfirmAsync("Transfers", "maio-corrigido.xlsx", File("8"));

        Assert.Equal(2, second.ReplacedRecords);
        var transfers = (await GetOverviewAsync(admin, a)).RecentTransfers;
        Assert.Equal(2, transfers.Count);
        Assert.All(transfers, t => Assert.Equal(8m, t.Quantity));
    }

    [Fact]
    public async Task Estoque_como_sai_do_erp_e_aceito_sem_tratar()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, _) = await CreateProductsAsync(admin);
        string[] raw = ["codigoproduto", "descricao", "qtde", "qtdemask", "qtd01", "qtdemask01", "qtd06", "qtdemask06", "qtd14", "qtdemask14", "precocompra"];
        var file = Xlsx(raw, [a, "PRODUTO A", "12", "12,0000", "4", "4,0000", "3", "3,0000", "5", "5,0000", "9.99"]);

        var report = await admin.ImportAndConfirmAsync("Stock", "quantidade por empresa.xlsx", file, DataDate);

        Assert.Equal(1, report.ValidRows);
        Assert.Contains(report.IssueGroups, g => g.Code == "stock.store_closed" && g.SampleValues.Contains("QTD14"));
        Assert.DoesNotContain(report.IssueGroups, g => g.Code == "stock.total_mismatch");
        var overview = await GetOverviewAsync(admin, a);
        Assert.Equal((4m, 3m), (Store(overview, "01").Stock!.Value, Store(overview, "06").Stock!.Value));
    }

    [Fact]
    public async Task Estoque_sem_tratar_cadastra_produto_novo_com_os_dados_do_proprio_arquivo()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, _) = await CreateProductsAsync(admin);
        var (novo, marcaNova) = (UniqueCode("790"), UniqueCode("6"));
        string[] raw = ["codigoproduto", "descricao", "unidade", "setor", "linha", "marca", "referencia", "qtde", "qtd06"];
        var file = Xlsx(raw,
            [a, "PRODUTO A", "PC", "BRINQUEDOS", "OUTROS", "000000", "R1", "1", "1"],
            [novo, $"PRODUTO NOVO {novo}", "UN", "BRINQUEDOS", "BONECAS", marcaNova, "REF-NOVO", "7", "7"]);

        var report = await admin.ImportAndConfirmAsync("Stock", "quantidade por empresa.xlsx", file, DataDate);

        Assert.Equal(2, report.ValidRows);
        Assert.Contains(report.IssueGroups, g => g.Code == "stock.product_created" && g.SampleValues.Any(v => v.StartsWith(novo)));
        Assert.Contains(report.IssueGroups, g => g.Code == "product.brand_placeholder");
        Assert.Equal(7m, Store(await GetOverviewAsync(admin, novo), "06").Stock);
        Assert.Equal(1m, Store(await GetOverviewAsync(admin, a), "06").Stock); // produto existente não é alterado
    }

    [Fact]
    public async Task Transferencias_como_saem_do_erp_usam_texto_e_usu()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, _) = await CreateProductsAsync(admin);
        var year = UniqueYear();
        string[] raw = ["mpcod", "mppro", "mpdat", "mpqtd", "mpent", "mpsai", "mptip", "mploja", "texto", "usu", "moori"];
        var file = Xlsx(raw,
            ["", "", "", "0", "0", "0", "", "", $"{a} - PRODUTO A", "", ""],
            ["0384161900", a, "", "2", "0", "2", "S", "10", $"     14/09/{year} - TRANSFERENCIA DE 10 PARA 05", "USUARIO2", "0001747900"],
            ["", "", "", "0", "0", "2", "", "", "TOTAL:", "", ""]);

        var report = await admin.ImportAndConfirmAsync("Transfers", "mov de produtos.xlsx", file);

        Assert.Equal(1, report.ValidRows);
        var transfer = Assert.Single((await GetOverviewAsync(admin, a)).RecentTransfers);
        Assert.Equal(("10", "05", "Out", "USUARIO2"), (transfer.OriginCode, transfer.DestinationCode, transfer.Direction, transfer.UserName));
    }

    [Fact]
    public async Task Transferencias_por_documento_gravam_so_os_recebidos_como_saida_e_entrada()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (a, b) = await CreateProductsAsync(admin);
        var year = UniqueYear();
        string[] headers = ["texto", "qtde", "vuniven", "vunicom", "vtotven", "vcod", "tiporeg", "prouni", "qtdemask"];
        var file = Xlsx(headers,
            [$"0001728300  01/09/{year} - Origem: [05] DEPOSITO - DO RE MI / Destino: [06] MOGI MIRIM 3 - DO RE MI", "48", "0", "0", "0", "0001728300", "1", "PC", ""],
            [" Usuário: USUARIO1 - Status: Recebido   [Total Compra: R$ 715,68]", "0", "0", "0", "0", "0001728300", "1", "PC", ""],
            [$"   {a} - PRODUTO A", "48", "59.99", "14.91", "2879.52", "0001728300", "0", "PC", "48,0000"],
            [$"0001729300  02/09/{year} - Origem: [05] DEPOSITO - DO RE MI / Destino: [07] ITAPIRA - DO RE MI", "5", "0", "0", "0", "0001729300", "1", "PC", ""],
            [" Usuário: USUARIO1 - Status: Pendente   [Total Compra: R$ 10,00]", "0", "0", "0", "0", "0001729300", "1", "PC", ""],
            [$"   {b} - PRODUTO B", "5", "1", "1", "5", "0001729300", "0", "PC", "5,0000"],
            [$"0001728800  03/09/{year} - Origem: [01] MATRIZ-DO RE MI / Destino: [05] DEPOSITO - DO RE MI", "2", "0", "0", "0", "0001728800", "1", "PC", ""],
            [" Usuário: USUARIO7 - Status:    [Total Compra: R$ 1,00]", "0", "0", "0", "0", "0001728800", "1", "PC", ""],
            [$"   {b} - PRODUTO B", "2", "1", "1", "2", "0001728800", "0", "PC", "2,0000"]);

        var report = await admin.ImportAndConfirmAsync("Transfers", "Transferencia.XLS", file);

        Assert.Equal(1, report.ValidRows);
        Assert.Contains(report.IssueGroups, g => g.Code == "transfer.document_not_received");
        Assert.Contains(report.IssueGroups, g => g.Code == "transfer.document_canceled");
        var transfers = (await GetOverviewAsync(admin, a)).RecentTransfers;
        Assert.Equal(["In", "Out"], transfers.Select(t => t.Direction).Order());
        Assert.All(transfers, t => Assert.Equal(("05", "06", 48m, "USUARIO1", new DateOnly(year, 9, 1)), (t.OriginCode, t.DestinationCode, t.Quantity, t.UserName, t.Date)));
        Assert.Empty((await GetOverviewAsync(admin, b)).RecentTransfers);
    }

    [Fact]
    public async Task Arquivo_de_outro_tipo_e_recusado_pelo_layout()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var brands = Xlsx(BrandHeaders, ["1", "MARCA", "1"]);

        var asStock = await (await admin.UploadAsync("Stock", "Marca.xlsx", brands, DataDate)).ReadAsync<ImportReportDto>();
        var asTransfers = await (await admin.UploadAsync("Transfers", "Marca.xlsx", brands)).ReadAsync<ImportReportDto>();

        Assert.Equal("Rejected", asStock.Status);
        Assert.Equal("Rejected", asTransfers.Status);
    }

    /// <summary>Cria uma marca e dois produtos novos (códigos únicos) e devolve os códigos.</summary>
    private static async Task<(string A, string B)> CreateProductsAsync(HttpClient admin)
    {
        var brand = UniqueCode("7");
        var (a, b) = (UniqueCode("789"), UniqueCode("789"));
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx", Xlsx(ProductHeaders,
            [a, $"PRODUTO A {a}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R1", ""],
            [b, $"PRODUTO B {b}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R2", ""]));
        return (a, b);
    }

    private static async Task<OverviewDto> GetOverviewAsync(HttpClient client, string productCode)
    {
        var products = await client.GetFromJsonAsync<PagedDto<ProductItem>>($"/api/products?search={productCode}", ApiClientExtensions.Json);
        var id = products!.Items.Single(p => p.Code == productCode).Id;
        return (await client.GetFromJsonAsync<OverviewDto>($"/api/products/{id}/overview", ApiClientExtensions.Json))!;
    }

    private static StoreRowDto Store(OverviewDto overview, string code) => overview.Stores.Single(s => s.StoreCode == code);

    /// <summary>Ano exclusivo do teste: a substituição por período não interfere nos outros testes do mesmo banco.</summary>
    private static int UniqueYear() => Random.Shared.Next(3000, 9000);

    private sealed record OverviewDto(DateOnly? StockDate, List<StoreRowDto> Stores, List<TransferDto> RecentTransfers);

    private sealed record StoreRowDto(
        string StoreCode, decimal? Stock, decimal? Sold12Months, DateOnly? SalesPeriodEnd, decimal? DailyAverage, decimal? CoverageDays);

    private sealed record TransferDto(
        DateOnly Date, bool IsCancellation, string OriginCode, string DestinationCode, string Direction, decimal Quantity, string? UserName);
}
