using System.Net;
using System.Net.Http.Json;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Sazonalidade (decisão 65): épocas, sugestões geradas pela análise e efeito nas sugestões de transferência.</summary>
[Collection(ApiCollection.Name)]
public sealed class SeasonalityTests(SmartStockApiFactory factory)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Um mês que não é o atual: a época de teste fica sempre fora do período hoje.</summary>
    private static readonly int OtherMonth = Today.Month % 12 + 1;

    [Fact]
    public async Task Epocas_aprovadas_vem_cadastradas_e_so_o_administrador_altera()
    {
        using var consulta = await factory.CreateClientForRoleAsync(Roles.Consulta);

        var seasons = (await consulta.GetFromJsonAsync<List<SeasonDto>>("/api/seasonality/seasons", ApiClientExtensions.Json))!;

        Assert.Contains(seasons, s => s.Name == "Natal" && s.Keywords.Contains("NATAL"));
        Assert.Contains(seasons, s => s.Name == "Copa do Mundo" && s.WorldCupOnly);
        Assert.Equal(HttpStatusCode.Forbidden, (await consulta.PostAsJsonAsync("/api/seasonality/seasons", NewSeason("X", "Y"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await consulta.PostAsJsonAsync("/api/seasonality/suggestions/decision", new { ids = new[] { 1L }, confirm = true })).StatusCode);
    }

    [Fact]
    public async Task Palavra_chave_gera_sugestao_e_confirmada_fora_da_epoca_tira_o_produto_das_sugestoes()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var keyword = UniqueCode("SZ");
        var season = await CreateSeasonAsync(admin, keyword);
        var product = await PrepareRuptureAsync(admin, $"BRINQUEDO {keyword} 30CM");
        await RunAnalysisAsync(admin);

        var suggestion = Assert.Single(await SuggestionsAsync(admin, season.Id));
        Assert.Equal((product, "Keyword"), (suggestion.Code, suggestion.Kind));
        Assert.Contains(keyword, suggestion.Evidence);
        Assert.Single(await TransfersAsync(admin, product)); // ainda não é sazonal: Mogi Mirim em ruptura recebe do Depósito

        (await admin.PostAsJsonAsync("/api/seasonality/suggestions/decision", new { ids = new[] { suggestion.Id }, confirm = true })).EnsureSuccessStatusCode();
        await RunAnalysisAsync(admin);

        var seasonal = Assert.Single((await admin.GetFromJsonAsync<PagedDto<SeasonalProductDto>>(
            $"/api/seasonality/products?seasonId={season.Id}", ApiClientExtensions.Json))!.Items);
        Assert.Equal((product, "OutOfSeason"), (seasonal.Code, seasonal.State));
        Assert.Empty(await TransfersAsync(admin, product));
        var position = Assert.Single((await admin.GetFromJsonAsync<PagedDto<PositionDto>>(
            $"/api/analysis/positions?search={product}&storeId={await StoreIdAsync(admin, "06")}", ApiClientExtensions.Json))!.Items);
        Assert.Equal(("NoMovement", "None"), (position.Situation, position.Priority)); // falta fora da época não é ruptura
    }

    [Fact]
    public async Task Filtro_por_epoca_so_traz_as_sugestoes_daquela_epoca()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (first, second) = (UniqueCode("SZ"), UniqueCode("SZ"));
        var seasonA = await CreateSeasonAsync(admin, first);
        var seasonB = await CreateSeasonAsync(admin, second);
        var (productA, productB) = await PrepareRuptureAsync(admin, $"ENFEITE {first}", $"ENFEITE {second}");
        await RunAnalysisAsync(admin);

        var onlyA = await SuggestionsAsync(admin, seasonA.Id);
        var onlyB = await SuggestionsAsync(admin, seasonB.Id);

        Assert.Equal(productA, Assert.Single(onlyA).Code);
        Assert.Equal(productB, Assert.Single(onlyB).Code);
        await admin.PostAsJsonAsync("/api/seasonality/suggestions/decision", new { seasonId = seasonA.Id, confirm = false });
        await admin.PostAsJsonAsync("/api/seasonality/suggestions/decision", new { seasonId = seasonB.Id, confirm = false });
    }

    [Fact]
    public async Task Sugestao_rejeitada_nao_volta_na_proxima_analise()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var keyword = UniqueCode("SZ");
        var season = await CreateSeasonAsync(admin, keyword);
        await PrepareRuptureAsync(admin, $"ENFEITE {keyword}");
        await RunAnalysisAsync(admin);

        var decision = await admin.PostAsJsonAsync("/api/seasonality/suggestions/decision", new { seasonId = season.Id, confirm = false });
        decision.EnsureSuccessStatusCode();
        await RunAnalysisAsync(admin);

        Assert.Empty(await SuggestionsAsync(admin, season.Id));
    }

    private static object NewSeason(string name, string keyword) =>
        new { name, startMonth = OtherMonth, endMonth = OtherMonth, worldCupOnly = false, keywords = new[] { keyword }, matchDescription = true, isActive = true };

    private static async Task<SeasonDto> CreateSeasonAsync(HttpClient admin, string keyword)
    {
        var response = await admin.PostAsJsonAsync("/api/seasonality/seasons", NewSeason($"Teste {keyword}", keyword));
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<SeasonDto>();
    }

    /// <summary>Produto novo: Mogi Mirim sem estoque vendendo 365/ano; Depósito com 100 un.</summary>
    private static async Task<string> PrepareRuptureAsync(HttpClient admin, string description) =>
        (await PrepareProductsAsync(admin, [description]))[0];

    private static async Task<(string, string)> PrepareRuptureAsync(HttpClient admin, string first, string second)
    {
        var products = await PrepareProductsAsync(admin, [first, second]);
        return (products[0], products[1]);
    }

    /// <summary>Os produtos entram no mesmo arquivo de estoque e de vendas: cada importação é uma foto completa.</summary>
    private static async Task<List<string>> PrepareProductsAsync(HttpClient admin, string[] descriptions)
    {
        var brand = UniqueCode("6");
        var products = descriptions.Select(_ => UniqueCode("793")).ToList();
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx", Xlsx(ProductHeaders,
            [.. products.Zip(descriptions, (p, d) => new[] { p, d, brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R", "" })]));
        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx",
            Xlsx(["Codigo do Produto", "05 DEPOSITO", "06 MOGI MIRIM"], [.. products.Select(p => new[] { p, "100", "0" })]), Today);
        await admin.ImportAndConfirmAsync("Sales", "vendas.xlsx",
            XlsxSheet("06 MOGI MIRIM", ["ivpro", "prodes", "valor", "qtde"], [.. products.Select(p => new[] { p, "P", "10", "365" })]), Today);
        return products;
    }

    private static async Task RunAnalysisAsync(HttpClient admin) => (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();

    private static async Task<List<SuggestionDto>> SuggestionsAsync(HttpClient client, int seasonId) =>
        (await client.GetFromJsonAsync<PagedDto<SuggestionDto>>($"/api/seasonality/suggestions?seasonId={seasonId}", ApiClientExtensions.Json))!.Items;

    private static async Task<List<TransferDto>> TransfersAsync(HttpClient client, string product) =>
        (await client.GetFromJsonAsync<PagedDto<TransferDto>>(
            $"/api/transfer-suggestions?status=Suggested&search={product}", ApiClientExtensions.Json))!.Items;

    private static async Task<int> StoreIdAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<List<StoreItem>>("/api/stores", ApiClientExtensions.Json))!.Single(s => s.Code == code).Id;

    private sealed record SeasonDto(int Id, string Name, bool WorldCupOnly, List<string> Keywords);

    private sealed record SuggestionDto(long Id, string Code, string Kind, string Evidence);

    private sealed record SeasonalProductDto(string Code, string State);

    private sealed record TransferDto(long Id);

    private sealed record PositionDto(string Situation, string Priority);
}
