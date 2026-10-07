using System.Globalization;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Common;
using SmartStock.Application.Imports;
using SmartStock.Domain.Imports;

namespace SmartStock.Api.Cli;

/// <summary>
/// Importação pela linha de comando, sem login (quem executa já tem acesso ao servidor).
/// Usa exatamente a mesma validação da tela e fica registrada no histórico e na auditoria como "linha de comando".
///
///   dotnet run --project backend/src/SmartStock.Api -- importar marcas "caminho\Marca.xlsx"
///   dotnet run --project backend/src/SmartStock.Api -- importar produtos "caminho\Produtos.xlsx" --confirmar
///   dotnet run --project backend/src/SmartStock.Api -- importar estoque "caminho\Estoque.xlsx" --data 2026-09-23 --confirmar
///   dotnet run --project backend/src/SmartStock.Api -- importar vendas "caminho\04 JAGUARIUNA.xlsx" --data 2026-09-23 [--loja 04]
///   dotnet run --project backend/src/SmartStock.Api -- importar transferencias "caminho\Transferencias.xlsx" --confirmar
///
/// Sem --confirmar, apenas valida e mostra o relatório. Estoque e vendas exigem --data (AAAA-MM-DD ou DD/MM/AAAA).
/// </summary>
internal static class ImportCommand
{
    public const string Name = "importar";
    public const string DetectName = "detectar";

    private static readonly Dictionary<string, ImportType> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["marcas"] = ImportType.Brands,
        ["produtos"] = ImportType.Products,
        ["estoque"] = ImportType.Stock,
        ["vendas"] = ImportType.Sales,
        ["transferencias"] = ImportType.Transfers,
        ["vendas-diarias"] = ImportType.DailySales
    };

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && (args[0].Equals(Name, StringComparison.OrdinalIgnoreCase) || args[0].Equals(DetectName, StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (args[0].Equals(DetectName, StringComparison.OrdinalIgnoreCase))
            return await DetectAsync(services, args);

        if (args.Length < 3 || !Types.TryGetValue(args[1], out var type))
        {
            Console.WriteLine($"Uso: importar <{string.Join("|", Types.Keys)}> <arquivo> [--data AAAA-MM-DD] [--loja 04] [--confirmar]");
            return 2;
        }

        var path = Path.GetFullPath(args[2]);
        if (!File.Exists(path))
        {
            Console.WriteLine($"Arquivo não encontrado: {path}");
            return 2;
        }

        if (!TryReadParameters(args, out var parameters))
            return 2;
        var confirm = args.Contains("--confirmar", StringComparer.OrdinalIgnoreCase);

        Result<ImportReport> uploaded;
        await using (var uploadScope = services.CreateAsyncScope())
        await using (var stream = File.OpenRead(path))
        {
            uploaded = await uploadScope.ServiceProvider.GetRequiredService<IImportService>()
                .UploadAsync(type, stream, Path.GetFileName(path), parameters);
        }
        if (!uploaded.IsSuccess)
        {
            Console.WriteLine($"Erro: {uploaded.Error!.Message}");
            return 1;
        }

        Print(uploaded.Value);
        if (uploaded.Value.Status != ImportStatus.Validated)
            return 1;

        if (!confirm)
        {
            Console.WriteLine("Somente validado. Para gravar, repita o comando com --confirmar (ou confirme na tela Importações).");
            return 0;
        }

        // Confirmação num escopo novo, como acontece na tela (outra requisição).
        await using var confirmScope = services.CreateAsyncScope();
        var confirmed = await confirmScope.ServiceProvider.GetRequiredService<IImportService>().ConfirmAsync(uploaded.Value.Id);
        if (!confirmed.IsSuccess)
        {
            Console.WriteLine($"Erro ao gravar: {confirmed.Error!.Message}");
            return 1;
        }

        Console.WriteLine($"Gravado: {confirmed.Value.ValidRows:N0} registros.");
        if (confirmed.Value.ReplacedRecords > 0)
            Console.WriteLine($"Substituídos: {confirmed.Value.ReplacedRecords:N0} registros do mesmo período.");
        return 0;
    }

    /// <summary>
    /// detectar <arquivo>: escreve o tipo pelo conteúdo (marcas, produtos, estoque, vendas, transferencias), para a pasta
    /// de entrega não depender do nome do arquivo. Sai com 3 quando nenhum tipo reconhece.
    /// </summary>
    private static async Task<int> DetectAsync(IServiceProvider services, string[] args)
    {
        if (args.Length < 2 || !File.Exists(args[1]))
        {
            Console.WriteLine("Uso: detectar <arquivo>");
            return 2;
        }
        await using var scope = services.CreateAsyncScope();
        var type = scope.ServiceProvider.GetRequiredService<IImportService>().DetectType(Path.GetFullPath(args[1]));
        Console.WriteLine(type is null ? "desconhecido" : Types.First(t => t.Value == type).Key);
        return type is null ? 3 : 0;
    }

    private static bool TryReadParameters(string[] args, out ImportParameters parameters)
    {
        parameters = ImportParameters.None;
        var rawDate = OptionValue(args, "--data");
        DateOnly? referenceDate = null;
        if (rawDate is not null)
        {
            if (!DateOnly.TryParseExact(rawDate, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                Console.WriteLine($"Data inválida: {rawDate}. Use AAAA-MM-DD ou DD/MM/AAAA.");
                return false;
            }
            referenceDate = parsed;
        }

        parameters = new ImportParameters(referenceDate, OptionValue(args, "--loja"));
        return true;
    }

    private static string? OptionValue(string[] args, string option)
    {
        var index = Array.FindIndex(args, a => a.Equals(option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void Print(ImportReport report)
    {
        Console.WriteLine($"{report.FileName}: {report.Status}");
        if (report.StoreCode is not null)
            Console.WriteLine($"  Loja: {report.StoreCode} {report.StoreName}");
        if (report.ReferenceDate is not null)
            Console.WriteLine($"  Data dos dados: {report.ReferenceDate:dd/MM/yyyy}");
        if (report.PeriodStart is not null)
            Console.WriteLine($"  Período: {report.PeriodStart:dd/MM/yyyy} a {report.PeriodEnd:dd/MM/yyyy}");
        if (report.RejectionReason is not null)
            Console.WriteLine($"  Recusado: {report.RejectionReason}");
        Console.WriteLine($"  Linhas {report.TotalRows:N0} | válidas {report.ValidRows:N0} | com erro {report.ErrorRows:N0} | avisos {report.WarningCount:N0}");
        foreach (var group in report.IssueGroups)
            Console.WriteLine($"  {(group.Severity == ImportIssueSeverity.Error ? "ERRO " : "AVISO")} {group.Count,7:N0}  {group.Message}  ex.: {string.Join(", ", group.SampleValues)}");
    }
}

/// <summary>Autor registrado nas importações feitas pela linha de comando.</summary>
internal sealed class CommandLineCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? Email => "linha de comando (servidor)";
    public string? IpAddress => "local";
}
