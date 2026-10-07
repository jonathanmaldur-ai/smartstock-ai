using System.Net;
using System.Net.Http.Json;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Chat de perguntas guiadas (decisão 41).</summary>
[Collection(ApiCollection.Name)]
public sealed class AssistantTests(SmartStockApiFactory factory)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public async Task Responde_rupturas_da_loja_com_tabela_e_o_que_entendeu()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin);

        var answer = await AskAsync(admin, $"rupturas {product} em mogi mirim");

        Assert.Contains("ruptura", answer.Understood);
        Assert.Contains("06 Mogi Mirim", answer.Understood);
        Assert.StartsWith("1 itens em ruptura", answer.Text);
        Assert.Contains(answer.Table!.Rows, r => r[0].Contains(product));
    }

    [Fact]
    public async Task Responde_transferencias_para_a_loja_e_onde_tem_o_produto()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var product = await PrepareRuptureAsync(admin);

        var transfers = await AskAsync(admin, $"o que mandar para mogi mirim do {product}");
        var stock = await AskAsync(admin, $"onde tem {product}");

        Assert.Contains(transfers.Table!.Rows, r => r[1] == "05 → 06" && r[2] == "30 un.");
        Assert.Contains("100 unidades na rede", stock.Text);
    }

    [Fact]
    public async Task Pergunta_sem_sentido_traz_exemplos_e_consulta_pode_perguntar()
    {
        using var consulta = await factory.CreateClientForRoleAsync(Roles.Consulta);

        var answer = await AskAsync(consulta, "oi");
        var empty = await consulta.PostAsJsonAsync("/api/assistant", new { question = "" });

        Assert.NotEmpty(answer.Suggestions);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    private static async Task<AnswerDto> AskAsync(HttpClient client, string question)
    {
        var response = await client.PostAsJsonAsync("/api/assistant", new { question });
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<AnswerDto>();
    }

    /// <summary>Produto novo: Mogi Mirim sem estoque vendendo 365/ano; Depósito com 100 un.; análise gerada.</summary>
    private static async Task<string> PrepareRuptureAsync(HttpClient admin)
    {
        var brand = UniqueCode("3");
        var product = UniqueCode("793");
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx",
            Xlsx(ProductHeaders, [product, $"PRODUTO {product}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R", ""]));
        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx", Xlsx(["Codigo do Produto", "05 DEPOSITO", "06 MOGI MIRIM"], [product, "100", "0"]), Today);
        await admin.ImportAndConfirmAsync("Sales", "vendas.xlsx", XlsxSheet("06 MOGI MIRIM", ["ivpro", "prodes", "valor", "qtde"], [product, "P", "10", "365"]), Today);
        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();
        return product;
    }

    private sealed record AnswerDto(string Text, string? Understood, TableDto? Table, List<string> Suggestions);

    private sealed record TableDto(List<string> Columns, List<List<string>> Rows);
}
