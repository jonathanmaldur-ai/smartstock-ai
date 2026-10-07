using Microsoft.Extensions.DependencyInjection;
using SmartStock.Application.Imports;
using SmartStock.Domain.Imports;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Decisão 50: o tipo da planilha vem das colunas, não do nome do arquivo.</summary>
[Collection(ApiCollection.Name)]
public sealed class ImportDetectionTests(SmartStockApiFactory factory)
{
    public static TheoryData<string[], ImportType?> Layouts => new()
    {
        { BrandHeaders, ImportType.Brands },
        { ProductHeaders, ImportType.Products },
        { ["codigoproduto", "descricao", "marca", "qtde", "qtd01", "qtd06"], ImportType.Stock },
        { ["Codigo do Produto", "05 DEPOSITO", "06 MOGI MIRIM"], ImportType.Stock },
        { ["ivpro", "prodes", "valor", "qtde"], ImportType.Sales },
        { ["vennum", "vendta", "venhor", "vencli", "ventot", "qtdtot", "venbru"], ImportType.DailySales },
        { ["texto", "qtde", "vuni", "vtot", "vendta", "vennum", "tiporeg", "prouni"], ImportType.DailySales },
        { ["texto", "qtde", "vuniven", "vcod", "tiporeg"], ImportType.Transfers },
        { ["mpent", "mpsai", "texto", "usu"], ImportType.Transfers },
        { ["Loja", "Cidade"], null }
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Reconhece_o_tipo_pelas_colunas_com_qualquer_nome(string[] headers, ImportType? expected)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qualquer nome {Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(path, Xlsx(headers, headers.Select(_ => "1").ToArray()));
        try
        {
            using var scope = factory.Services.CreateScope();
            Assert.Equal(expected, scope.ServiceProvider.GetRequiredService<IImportService>().DetectType(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
