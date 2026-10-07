using System.Net;
using SmartStock.Tests.Infrastructure;

namespace SmartStock.Tests.Api;

/// <summary>Em produção a API entrega a interface compilada (wwwroot) e devolve o index.html nas rotas da interface.</summary>
[Collection(ApiCollection.Name)]
public sealed class StaticFilesTests(SmartStockApiFactory factory)
{
    [Fact]
    public async Task Asset_da_interface_e_entregue_como_arquivo_e_nao_como_index()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync(SmartStockApiFactory.FakeAssetPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Rota_da_interface_devolve_o_index_html()
    {
        using var client = factory.CreateHttpsClient();

        var response = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
