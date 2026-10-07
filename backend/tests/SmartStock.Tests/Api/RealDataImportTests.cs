using SmartStock.Tests.Infrastructure;
using Xunit.Abstractions;

namespace SmartStock.Tests.Api;

/// <summary>
/// Conferência com as planilhas reais da pasta "04 - Dados" (não versionadas).
/// Fica fora da execução normal. Para rodar: dotnet test --filter "Category=DadosReais"
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Category", "DadosReais")]
public sealed class RealDataImportTests(SmartStockApiFactory factory, ITestOutputHelper output)
{
    [Fact]
    public async Task Importa_marcas_e_produtos_reais()
    {
        var dataFolder = FindDataFolder();
        if (dataFolder is null)
        {
            output.WriteLine("Pasta \"04 - Dados\" não encontrada: teste ignorado.");
            return;
        }

        using var admin = factory.CreateHttpsClient();
        admin.Timeout = TimeSpan.FromMinutes(10);
        await admin.SignInAsync(SmartStockApiFactory.AdminEmail, SmartStockApiFactory.AdminPassword);

        foreach (var (type, file) in new[] { ("Brands", "Marca.xlsx"), ("Products", "Produtos.xlsx") })
        {
            var started = DateTime.UtcNow;
            var report = await admin.ImportAndConfirmAsync(type, file, await File.ReadAllBytesAsync(Path.Combine(dataFolder, file)));

            output.WriteLine($"{file}: {report.Status} em {(DateTime.UtcNow - started).TotalSeconds:N1}s | " +
                             $"linhas {report.TotalRows} | gravadas {report.ValidRows} | com erro {report.ErrorRows} | avisos {report.WarningCount}");
            foreach (var group in report.IssueGroups)
                output.WriteLine($"   {group.Severity,-8} {group.Count,6}  {group.Code}  ex.: {string.Join(" | ", group.SampleValues)}");

            Assert.Equal("Confirmed", report.Status);
        }
    }

    private static string? FindDataFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "04 - Dados");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
