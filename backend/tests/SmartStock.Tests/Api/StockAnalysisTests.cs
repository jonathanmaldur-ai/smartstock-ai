using System.Net;
using System.Net.Http.Json;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Módulo 2.3: análise, sugestões de transferência e aprovação (decisões 8, 26 e 35).</summary>
[Collection(ApiCollection.Name)]
public sealed class StockAnalysisTests(SmartStockApiFactory factory)
{
    private static readonly string[] StockHeaders = ["Codigo do Produto", "01 MATRIZ", "05 DEPOSITO", "06 MOGI MIRIM"];
    private static readonly string[] SalesHeaders = ["ivpro", "prodes", "valor", "qtde"];

    /// <summary>Data de hoje: sem dias de projeção, os números do teste ficam exatos.</summary>
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Ruptura_gera_sugestao_do_deposito_com_motivo_e_dados_usados()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin);

        var summary = await RunAnalysisAsync(admin);
        var suggestion = Assert.Single(await SuggestionsAsync(admin, product, "Suggested"));

        Assert.NotNull(summary.AnalysisId);
        Assert.False(summary.IsOutdated);
        Assert.Equal(("05", "06", 30m), (suggestion.OriginCode, suggestion.DestinationCode, suggestion.Quantity));
        Assert.Equal("Critical", suggestion.Priority);
        Assert.Contains("está sem estoque", suggestion.Reason);
        Assert.Equal(1m, suggestion.DestinationDailyAverage);
        Assert.Equal(30m, suggestion.DestinationCoverageAfter);
    }

    [Fact]
    public async Task Aprovar_registra_a_decisao_e_a_proxima_analise_nao_repete_a_sugestao()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin);
        await RunAnalysisAsync(admin);
        var pending = Assert.Single(await SuggestionsAsync(admin, product, "Suggested"));

        var decision = await admin.PostAsJsonAsync("/api/transfer-suggestions/decision",
            new { ids = new[] { pending.Id }, approve = true, note = "Separar na segunda" });
        decision.EnsureSuccessStatusCode();

        var approved = Assert.Single(await SuggestionsAsync(admin, product, "Approved"));
        Assert.Equal((SmartStockApiFactory.AdminEmail, "Separar na segunda"), (approved.DecidedByEmail, approved.DecisionNote));

        await RunAnalysisAsync(admin);
        Assert.Empty(await SuggestionsAsync(admin, product, "Suggested")); // os 30 aprovados já estão "a caminho"
    }

    [Fact]
    public async Task Nova_analise_substitui_as_sugestoes_pendentes_e_rejeitadas_ficam_no_historico()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin);
        await RunAnalysisAsync(admin);
        var first = Assert.Single(await SuggestionsAsync(admin, product, "Suggested"));

        await RunAnalysisAsync(admin);

        Assert.Contains(await SuggestionsAsync(admin, product, "Superseded"), s => s.Id == first.Id);
        var second = Assert.Single(await SuggestionsAsync(admin, product, "Suggested"));
        (await admin.PostAsJsonAsync("/api/transfer-suggestions/decision", new { ids = new[] { second.Id }, approve = false })).EnsureSuccessStatusCode();
        Assert.Single(await SuggestionsAsync(admin, product, "Rejected"));

        var again = await admin.PostAsJsonAsync("/api/transfer-suggestions/decision", new { ids = new[] { second.Id }, approve = true });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Produto_que_vende_menos_que_12_por_ano_nao_gera_sugestao()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin, soldPerYear: "11");

        await RunAnalysisAsync(admin);

        Assert.Empty(await SuggestionsAsync(admin, product, "Suggested"));
        var positions = await admin.GetFromJsonAsync<PagedDto<PositionDto>>(
            $"/api/analysis/positions?search={product}&storeId={await StoreIdAsync(admin, "06")}", ApiClientExtensions.Json);
        var position = Assert.Single(positions!.Items);
        Assert.Equal(("Rupture", "Low"), (position.Situation, position.Priority));
    }

    [Fact]
    public async Task Somente_administrador_e_gerente_aprovam_e_consulta_nao_gera_analise()
    {
        using var operador = await factory.CreateClientForRoleAsync(Roles.Operador);
        using var consulta = await factory.CreateClientForRoleAsync(Roles.Consulta);
        using var gerente = await factory.CreateClientForRoleAsync(Roles.Gerente);
        var body = new { ids = new[] { 1L }, approve = true };

        Assert.Equal(HttpStatusCode.Forbidden, (await operador.PostAsJsonAsync("/api/transfer-suggestions/decision", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await consulta.PostAsync("/api/analysis", null)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await gerente.PostAsJsonAsync("/api/transfer-suggestions/decision", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await consulta.GetAsync("/api/transfer-suggestions")).StatusCode);
    }

    [Fact]
    public async Task Sugestoes_sao_exportadas_em_excel()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin);
        await RunAnalysisAsync(admin);

        var response = await admin.GetAsync($"/api/transfer-suggestions/export?status=Suggested&search={product}");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        using var workbook = new ClosedXML.Excel.XLWorkbook(await response.Content.ReadAsStreamAsync());
        Assert.Equal(product, workbook.Worksheet(1).Cell(2, 5).GetString());
    }

    [Fact]
    public async Task Parametros_fora_de_ordem_sao_recusados_e_alteracao_marca_a_analise_como_desatualizada()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var original = (await admin.GetFromJsonAsync<ParametersDto>("/api/analysis/parameters", ApiClientExtensions.Json))!;

        var invalid = await admin.PutAsJsonAsync("/api/analysis/parameters", original with { MinimumDays = 40 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        await RunAnalysisAsync(admin);
        (await admin.PutAsJsonAsync("/api/analysis/parameters", original with { MinimumAnnualSales = 24 })).EnsureSuccessStatusCode();
        var summary = (await admin.GetFromJsonAsync<SummaryDto>("/api/analysis", ApiClientExtensions.Json))!;
        (await admin.PutAsJsonAsync("/api/analysis/parameters", original)).EnsureSuccessStatusCode();

        Assert.True(summary.IsOutdated);
    }

    [Fact]
    public async Task Rota_agrupa_as_sugestoes_e_pode_ser_aprovada_inteira()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin);
        await RunAnalysisAsync(admin);
        var (deposito, mogiMirim) = (await StoreIdAsync(admin, "05"), await StoreIdAsync(admin, "06"));

        var routes = (await admin.GetFromJsonAsync<List<RouteDto>>(
            $"/api/transfer-suggestions/routes?status=Suggested&search={product}", ApiClientExtensions.Json))!;
        var route = Assert.Single(routes);
        Assert.Equal(("05", "06", 1, 30m, 1), (route.OriginCode, route.DestinationCode, route.Count, route.Units, route.CriticalCount));
        Assert.Equal(($"PRODUTO {product}", 30m), (route.TopProducts[0].Description, route.TopProducts[0].Quantity));

        var decision = await admin.PostAsJsonAsync("/api/transfer-suggestions/decision",
            new { approve = true, originStoreId = deposito, destinationStoreId = mogiMirim });
        decision.EnsureSuccessStatusCode();

        Assert.Single(await SuggestionsAsync(admin, product, "Approved"));
    }

    [Fact]
    public async Task Destino_negativo_mostra_o_estoque_importado_e_pode_ser_ocultado()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin, mogiMirimStock: "-6");
        await RunAnalysisAsync(admin);

        var suggestion = Assert.Single(await SuggestionsAsync(admin, product, "Suggested"));
        var hidden = (await admin.GetFromJsonAsync<PagedDto<SuggestionDto>>(
            $"/api/transfer-suggestions?status=Suggested&search={product}&hideNegativeDestination=true", ApiClientExtensions.Json))!;

        Assert.Equal(-6m, suggestion.DestinationStock);
        Assert.Empty(hidden.Items);
    }

    [Fact]
    public async Task Produto_sazonal_nao_gera_sugestao()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin);
        var products = await admin.GetFromJsonAsync<PagedDto<ProductItem>>($"/api/products?search={product}", ApiClientExtensions.Json);
        var id = products!.Items.Single().Id;

        (await admin.PutAsJsonAsync($"/api/products/{id}/seasonal", new { seasonal = true })).EnsureSuccessStatusCode();
        await RunAnalysisAsync(admin);

        Assert.Empty(await SuggestionsAsync(admin, product, "Suggested"));
        using var consulta = await factory.CreateClientForRoleAsync(Roles.Consulta);
        Assert.Equal(HttpStatusCode.Forbidden, (await consulta.PutAsJsonAsync($"/api/products/{id}/seasonal", new { seasonal = false })).StatusCode);
    }

    [Fact]
    public async Task Aprovada_vira_realizada_quando_a_saida_aparece_no_arquivo_de_transferencias()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin);
        await RunAnalysisAsync(admin);
        var pending = Assert.Single(await SuggestionsAsync(admin, product, "Suggested"));
        (await admin.PostAsJsonAsync("/api/transfer-suggestions/decision", new { ids = new[] { pending.Id }, approve = true })).EnsureSuccessStatusCode();

        // Saída anterior à aprovação não conta; a de hoje, na mesma rota, sim.
        string[] headers = ["DESCRIÇÃO DA TRANSFERENCIA", "ENTRADA", "SAIDA", "USUARIO", "STATUS TRANSFERENCIA"];
        var yesterday = Today.AddDays(-1).ToString("dd/MM/yyyy");
        await admin.ImportAndConfirmAsync("Transfers", "Transferencias.xlsx", Xlsx(headers,
            [$"{product} - PRODUTO", "0", "0", "", ""],
            [$"     {yesterday} - TRANSFERENCIA DE 05 PARA 06", "0", "5", "USUARIO1", "TRANSFERENCIA"],
            [$"     {Today:dd/MM/yyyy} - TRANSFERENCIA DE 05 PARA 06", "0", "28", "USUARIO1", "TRANSFERENCIA"],
            ["TOTAL:", "0", "33", "", ""]));
        await RunAnalysisAsync(admin);

        var completed = Assert.Single(await SuggestionsAsync(admin, product, "Completed"));
        Assert.Equal((Today, 28m), (completed.CompletedOn, completed.CompletedQuantity));
    }

    /// <summary>Produto novo: Mogi Mirim sem estoque vendendo 365/ano (1/dia); Depósito com 100 un.</summary>
    private static async Task<string> PrepareRuptureAsync(HttpClient admin, string soldPerYear = "365", string mogiMirimStock = "0")
    {
        var brand = UniqueCode("6");
        var product = UniqueCode("790");
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx",
            Xlsx(ProductHeaders, [product, $"PRODUTO {product}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R", ""]));
        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx", Xlsx(StockHeaders, [product, "0", "100", mogiMirimStock]), Today);
        await admin.ImportAndConfirmAsync("Sales", "vendas.xlsx", XlsxSheet("06 MOGI MIRIM", SalesHeaders, [product, "P", "10", soldPerYear]), Today);
        return product;
    }

    private static async Task<SummaryDto> RunAnalysisAsync(HttpClient admin)
    {
        var response = await admin.PostAsync("/api/analysis", null);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<SummaryDto>();
    }

    private static async Task<List<SuggestionDto>> SuggestionsAsync(HttpClient client, string product, string status) =>
        (await client.GetFromJsonAsync<PagedDto<SuggestionDto>>(
            $"/api/transfer-suggestions?status={status}&search={product}&pageSize=100", ApiClientExtensions.Json))!.Items;

    private static async Task<int> StoreIdAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<List<StoreItem>>("/api/stores", ApiClientExtensions.Json))!.Single(s => s.Code == code).Id;

    private sealed record SummaryDto(Guid? AnalysisId, bool IsOutdated);

    private sealed record SuggestionDto(
        long Id, string OriginCode, string DestinationCode, decimal Quantity, string Priority, string Status, string Reason,
        decimal DestinationStock,
        decimal DestinationDailyAverage, decimal? DestinationCoverageAfter, string? DecidedByEmail, string? DecisionNote,
        DateOnly? CompletedOn = null, decimal? CompletedQuantity = null);

    private sealed record PositionDto(string Situation, string Priority);

    private sealed record RouteDto(string OriginCode, string DestinationCode, int Count, decimal Units, int CriticalCount, List<PreviewDto> TopProducts);

    private sealed record PreviewDto(string Description, decimal Quantity);

    private sealed record ParametersDto(
        int CriticalCoverageDays, int MinimumDays, int IdealDays, int MaximumDays, int ExcessDays, decimal MinimumAnnualSales);
}
