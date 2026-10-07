using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Users;
using SmartStock.Infrastructure.Persistence;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Central de alertas (decisão 47): compara a análise atual com a anterior.</summary>
[Collection(ApiCollection.Name)]
public sealed class AlertTests(SmartStockApiFactory factory)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Venda_diaria_sem_reposicao_vira_alerta_e_o_ja_vi_continua_na_proxima_analise()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareDropAsync(admin);

        var alerts = await ListAsync(admin, product);
        var alert = Assert.Single(alerts.Items, a => a.Type == "SoldWithoutReplenishment");
        Assert.Equal(("06", "High"), (alert.StoreCode, alert.Priority));
        Assert.Contains("caiu de 20 para 0", alert.Message);

        using var consulta = await factory.CreateClientForRoleAsync(Roles.Consulta);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,
            (await consulta.PostAsJsonAsync("/api/alerts/seen", new { ids = new[] { alert.Id } })).StatusCode);

        (await admin.PostAsJsonAsync("/api/alerts/seen", new { ids = new[] { alert.Id } })).EnsureSuccessStatusCode();
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();

        Assert.DoesNotContain((await ListAsync(admin, product)).Items, a => a.Type == "SoldWithoutReplenishment");
        var withSeen = await ListAsync(admin, product, includeSeen: true);
        Assert.NotNull(Assert.Single(withSeen.Items, a => a.Type == "SoldWithoutReplenishment").SeenAt);
    }

    [Fact]
    public async Task Resumo_traz_todos_os_tipos_e_a_data_comparada()
    {
        using var admin = await factory.CreateAdminClientAsync();
        await PrepareDropAsync(admin);

        var summary = (await admin.GetFromJsonAsync<SummaryDto>("/api/alerts/summary", ApiClientExtensions.Json))!;

        Assert.Equal(Enum.GetValues<AlertType>().Length, summary.Types.Count);
        Assert.Equal(Today, summary.StockDate);
        Assert.True(summary.PreviousStockDate < Today);
        Assert.True(summary.Types.Single(t => t.Type == "SoldWithoutReplenishment").Total >= 1);
    }

    [Fact]
    public async Task Com_o_email_de_alertas_ligado_a_analise_continua_funcionando()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var parameters = (await admin.GetFromJsonAsync<Dictionary<string, object>>("/api/analysis/parameters", ApiClientExtensions.Json))!;
        var input = new
        {
            criticalCoverageDays = 7, minimumDays = 15, idealDays = 30, maximumDays = 60, excessDays = 120, minimumAnnualSales = 12,
            sendAlertEmail = true
        };
        try
        {
            (await admin.PutAsJsonAsync("/api/analysis/parameters", input)).EnsureSuccessStatusCode();
            var saved = (await admin.GetFromJsonAsync<ParametersDto>("/api/analysis/parameters", ApiClientExtensions.Json))!;
            Assert.True(saved.SendAlertEmail);

            await PrepareDropAsync(admin);
        }
        finally
        {
            (await admin.PutAsJsonAsync("/api/analysis/parameters", input with { sendAlertEmail = false })).EnsureSuccessStatusCode();
        }
        Assert.NotEmpty(parameters);
    }

    /// <summary>
    /// Ontem: Mogi Mirim com 20 un. (e uma análise dessa foto). Hoje: 0 un., vendendo 730/ano, sem transferência recebida.
    /// </summary>
    private async Task<string> PrepareDropAsync(HttpClient admin)
    {
        var brand = UniqueCode("2");
        var product = UniqueCode("796");
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx",
            Xlsx(ProductHeaders, [product, $"PRODUTO {product}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R", ""]));
        await admin.ImportAndConfirmAsync("Sales", "vendas.xlsx", XlsxSheet("06 MOGI MIRIM", ["ivpro", "prodes", "valor", "qtde"], [product, "P", "10", "730"]), Today);

        var yesterday = await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx",
            Xlsx(["Codigo do Produto", "05 DEPOSITO", "06 MOGI MIRIM"], [product, "0", "20"]), Today.AddDays(-1));
        await AddPreviousAnalysisAsync(yesterday.Id, Today.AddDays(-1));

        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx", Xlsx(["Codigo do Produto", "05 DEPOSITO", "06 MOGI MIRIM"], [product, "0", "0"]), Today);
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();
        return product;
    }

    /// <summary>Análise da foto de ontem, direto no banco: a análise pela API sempre usa a foto mais recente.</summary>
    private async Task AddPreviousAnalysisAsync(Guid stockImportId, DateOnly stockDate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartStockDbContext>();
        db.StockAnalyses.Add(new StockAnalysis
        {
            Id = Guid.NewGuid(), StockImportId = stockImportId, StockDate = stockDate, AnalysisDate = stockDate,
            CreatedAt = DateTimeOffset.UtcNow, CreatedByEmail = "teste"
        });
        await db.SaveChangesAsync();
    }

    private static async Task<PagedDto<AlertDto>> ListAsync(HttpClient client, string product, bool includeSeen = false) =>
        (await client.GetFromJsonAsync<PagedDto<AlertDto>>($"/api/alerts?search={product}&includeSeen={includeSeen}", ApiClientExtensions.Json))!;

    private sealed record AlertDto(long Id, string Type, string Priority, string? StoreCode, string Message, DateTimeOffset? SeenAt);

    private sealed record SummaryDto(DateOnly? StockDate, DateOnly? PreviousStockDate, List<TypeDto> Types);

    private sealed record TypeDto(string Type, int Total);

    private sealed record ParametersDto(bool SendAlertEmail);
}
