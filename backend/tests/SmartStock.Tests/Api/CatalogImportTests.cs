using System.Net;
using System.Net.Http.Json;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

[Collection(ApiCollection.Name)]
public sealed class CatalogImportTests(SmartStockApiFactory factory)
{
    [Fact]
    public async Task Unidades_da_rede_vem_cadastradas_com_as_lojas_fechadas()
    {
        using var client = await factory.CreateClientForRoleAsync(Roles.Consulta);

        var stores = (await client.GetFromJsonAsync<List<StoreItem>>("/api/stores", ApiClientExtensions.Json))!;

        Assert.Equal(12, stores.Count(s => s.Status == "Active"));
        Assert.Equal(["02", "03", "13", "14", "15", "16"], stores.Where(s => s.Status == "Closed").Select(s => s.Code).Order());
        Assert.Contains(stores, s => s is { Code: "01", City: "Mogi Guaçu" });
        Assert.Contains(stores, s => s is { Code: "05", Type: "Warehouse" });
    }

    [Fact]
    public async Task Categorias_iniciais_tem_correcao_e_exclusoes_da_decisao_23()
    {
        using var client = await factory.CreateClientForRoleAsync(Roles.Consulta);

        var categories = (await client.GetFromJsonAsync<List<CategoryItem>>("/api/categories", ApiClientExtensions.Json))!;

        Assert.Contains("BRIQUEDOS", categories.Single(c => c.Name == "BRINQUEDOS").Aliases);
        Assert.True(categories.Single(c => c.Name == "BEBIDAS").ExcludedFromAnalysis);
        Assert.True(categories.Single(c => c.Name == "VALE PRESENTE").ExcludedFromAnalysis);
        Assert.False(categories.Single(c => c.Name == "ALIMENTÍCIO").ExcludedFromAnalysis);
    }

    [Fact]
    public async Task Marcas_so_sao_gravadas_depois_da_confirmacao()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var code = UniqueCode("9");
        var file = Xlsx(BrandHeaders,
            [code, $"MARCA TESTE {code}", "1"],
            [code, "REPETIDA", "1"],
            [UniqueCode("8"), null, "1"]);

        var upload = await admin.UploadAsync("Brands", "Marca.xlsx", file);
        var report = await upload.ReadAsync<ImportReportDto>();

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal("Validated", report.Status);
        Assert.Equal((3, 1, 2), (report.TotalRows, report.ValidRows, report.ErrorRows));
        Assert.Contains(report.IssueGroups, g => g.Code == "brand.duplicated");
        Assert.Contains(report.IssueGroups, g => g.Code == "brand.name_missing");
        Assert.Equal(0, (await SearchBrandsAsync(admin, code)).TotalCount);

        (await admin.PostAsync($"/api/imports/{report.Id}/confirm", null)).EnsureSuccessStatusCode();

        var brands = await SearchBrandsAsync(admin, code);
        Assert.Equal($"MARCA TESTE {code}", brands.Items.Single().Name);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/imports/{report.Id}/confirm", null)).StatusCode);
    }

    [Fact]
    public async Task Produtos_usam_a_correcao_de_categorias_e_preservam_zeros_a_esquerda()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var brand = UniqueCode("7");
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, "MARCA PRODUTOS", "1"]));

        var zeroCode = "00000" + UniqueCode("1");
        var unknownBrand = UniqueCode("0");
        var newCategory = $"CATEGORIA NOVA {brand}";
        var file = Xlsx(ProductHeaders,
            [zeroCode, "PRODUTO COM ZEROS", brand, "PC", "1", "BRIQUEDOS_x0002_", "EDUCATIVO", "REF1", "OUTROS BRINQUEDOS"],
            [UniqueCode("2"), "PRODUTO CATEGORIA NOVA", brand, "UN", "0", newCategory, null, null, null],
            [UniqueCode("3"), "PRODUTO SEM MARCA", unknownBrand, "PC", "1", "BRINQUEDOS", null, null, null],
            [UniqueCode("3"), "PRODUTO SEM CODIGO DE MARCA", null, "PC", "1", "BRINQUEDOS", null, null, null]);

        var upload = await admin.UploadAsync("Products", "Produtos.xlsx", file);
        var report = await upload.ReadAsync<ImportReportDto>();
        Assert.Equal((4, 3, 1), (report.TotalRows, report.ValidRows, report.ErrorRows));
        Assert.Contains(report.IssueGroups, g => g is { Code: "product.brand_missing", Severity: "Error" });
        Assert.Contains(report.IssueGroups, g => g is { Code: "product.brand_placeholder", Severity: "Warning" });
        Assert.Contains(report.IssueGroups, g => g is { Code: "product.category_new", Severity: "Warning" });
        (await admin.PostAsync($"/api/imports/{report.Id}/confirm", null)).EnsureSuccessStatusCode();

        var products = await admin.GetFromJsonAsync<PagedDto<ProductItem>>(
            $"/api/products?search={zeroCode}", ApiClientExtensions.Json);
        var product = products!.Items.Single();
        Assert.Equal(zeroCode, product.Code);
        Assert.Equal("BRINQUEDOS", product.CategoryName);
        Assert.Equal("EDUCATIVO", product.SubcategoryName);

        var categories = await admin.GetFromJsonAsync<List<CategoryItem>>("/api/categories", ApiClientExtensions.Json);
        Assert.Contains(categories!, c => c.Name == newCategory && c.ProductCount == 1);

        var placeholder = (await SearchBrandsAsync(admin, unknownBrand)).Items.Single();
        Assert.Equal($"MARCA {unknownBrand} (SEM CADASTRO)", placeholder.Name);
        Assert.Equal(1, placeholder.ProductCount);
    }

    [Fact]
    public async Task Reenviar_o_mesmo_arquivo_ja_confirmado_e_sinalizado()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var file = Xlsx(BrandHeaders, [UniqueCode("6"), "MARCA DUPLICADA", "1"]);
        var first = await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", file);

        var second = await (await admin.UploadAsync("Brands", "Marca (1).xlsx", file)).ReadAsync<ImportReportDto>();

        Assert.Equal(first.Id, second.DuplicateOfBatchId);
    }

    [Fact]
    public async Task Arquivo_cortado_no_limite_do_ERP_e_recusado()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var rows = Enumerable.Range(1, 65_534).Select(i => new[] { $"C{i}", $"MARCA {i}", "1" });

        var report = await (await admin.UploadAsync("Brands", "Marca.csv", Csv(BrandHeaders, rows))).ReadAsync<ImportReportDto>();

        Assert.Equal("Rejected", report.Status);
        Assert.Contains("cortado", report.RejectionReason);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/imports/{report.Id}/confirm", null)).StatusCode);
    }

    [Fact]
    public async Task Arquivo_com_layout_errado_e_recusado()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var report = await (await admin.UploadAsync("Products", "Marca.xlsx", Xlsx(BrandHeaders, ["1", "X", "1"])))
            .ReadAsync<ImportReportDto>();

        Assert.Equal("Rejected", report.Status);
        Assert.Contains("Codigo do Produto", report.RejectionReason);
    }

    [Fact]
    public async Task Arquivo_de_tipo_nao_suportado_e_recusado()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var response = await admin.UploadAsync("Brands", "marcas.pdf", [1, 2, 3]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unificar_categorias_move_os_produtos_e_vale_nas_proximas_importacoes()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var brand = UniqueCode("5");
        var source = $"ORIGEM {brand}";
        var target = $"DESTINO {brand}";
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, "MARCA UNIFICACAO", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx", Xlsx(ProductHeaders,
            [UniqueCode("41"), "PRODUTO A", brand, "PC", "1", source, null, null, null],
            [UniqueCode("42"), "PRODUTO B", brand, "PC", "1", target, null, null, null]));

        var categories = (await admin.GetFromJsonAsync<List<CategoryItem>>("/api/categories", ApiClientExtensions.Json))!;
        var sourceId = categories.Single(c => c.Name == source).Id;
        var targetId = categories.Single(c => c.Name == target).Id;

        var merge = await admin.PostAsJsonAsync($"/api/categories/{sourceId}/merge", new { targetId });
        merge.EnsureSuccessStatusCode();
        var merged = await merge.ReadAsync<CategoryItem>();
        Assert.Equal(2, merged.ProductCount);

        var next = await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx", Xlsx(ProductHeaders,
            [UniqueCode("43"), "PRODUTO C", brand, "PC", "1", source.ToLowerInvariant(), null, null, null]));
        Assert.DoesNotContain(next.IssueGroups, g => g.Code == "product.category_new");

        categories = (await admin.GetFromJsonAsync<List<CategoryItem>>("/api/categories", ApiClientExtensions.Json))!;
        Assert.DoesNotContain(categories, c => c.Name == source);
        Assert.Equal(3, categories.Single(c => c.Id == targetId).ProductCount);
    }

    [Fact]
    public async Task Ocorrencias_podem_ser_baixadas_em_CSV()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var report = await (await admin.UploadAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [null, "SEM CODIGO", "1"])))
            .ReadAsync<ImportReportDto>();

        var csv = await admin.GetStringAsync($"/api/imports/{report.Id}/issues.csv");

        Assert.StartsWith("﻿Linha;Coluna;Tipo", csv);
        Assert.Contains(";Erro;brand.code_missing;", csv);
    }

    [Fact]
    public async Task Descartar_nao_grava_nada()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var code = UniqueCode("4");
        var report = await (await admin.UploadAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [code, "DESCARTADA", "1"])))
            .ReadAsync<ImportReportDto>();

        var discard = await admin.PostAsync($"/api/imports/{report.Id}/discard", null);

        Assert.Equal("Discarded", (await discard.ReadAsync<ImportReportDto>()).Status);
        Assert.Equal(0, (await SearchBrandsAsync(admin, code)).TotalCount);
    }

    [Theory]
    [InlineData(Roles.Operador, HttpStatusCode.OK)]
    [InlineData(Roles.Gerente, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Consulta, HttpStatusCode.Forbidden)]
    public async Task Somente_Administrador_e_Operador_importam(string role, HttpStatusCode expected)
    {
        using var client = await factory.CreateClientForRoleAsync(role);

        var response = await client.UploadAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [UniqueCode("3"), "X", "1"]));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Somente_Administrador_altera_cadastros()
    {
        using var client = await factory.CreateClientForRoleAsync(Roles.Operador);
        var store = (await client.GetFromJsonAsync<List<StoreItem>>("/api/stores", ApiClientExtensions.Json))!.First();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync($"/api/stores/{store.Id}", new { name = "Outro", city = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync("/api/categories/1/exclusion", new { excluded = true })).StatusCode);
    }

    private static async Task<PagedDto<BrandItem>> SearchBrandsAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<PagedDto<BrandItem>>($"/api/brands?search={code}", ApiClientExtensions.Json))!;
}
