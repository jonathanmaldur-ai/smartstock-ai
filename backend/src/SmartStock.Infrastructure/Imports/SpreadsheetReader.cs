using System.Globalization;
using System.Text;
using ExcelDataReader;
using SmartStock.Domain.Catalog;

namespace SmartStock.Infrastructure.Imports;

/// <summary>
/// Lê a primeira aba de arquivos .xls, .xlsx ou .csv. A primeira linha não vazia é o cabeçalho;
/// as colunas são localizadas pelo nome (sem acento e sem diferença de maiúsculas).
/// </summary>
internal static class SpreadsheetReader
{
    /// <summary>Limite de linhas do formato antigo do Excel: um arquivo com exatamente esse total foi cortado na exportação.</summary>
    public const int LegacyExcelRowLimit = 65_535;

    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xls", ".xlsx", ".csv" };

    static SpreadsheetReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static SheetData Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? ExcelReaderFactory.CreateCsvReader(stream, new ExcelReaderConfiguration { FallbackEncoding = Encoding.GetEncoding(1252) })
            : ExcelReaderFactory.CreateReader(stream);

        string[]? headers = null;
        var rows = new List<SheetRow>();
        var lineNumber = 0;

        while (reader.Read())
        {
            lineNumber++;
            var cells = new string?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                cells[i] = CellToText(reader.GetValue(i));

            if (cells.All(string.IsNullOrWhiteSpace))
                continue;

            if (headers is null)
            {
                headers = cells.Select(c => c is null ? string.Empty : CatalogText.Key(c)).ToArray();
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var i = 0; i < headers.Length && i < cells.Length; i++)
            {
                if (headers[i].Length > 0)
                    values.TryAdd(headers[i], cells[i]);
            }
            rows.Add(new SheetRow(lineNumber, values));
        }

        return new SheetData(headers ?? [], rows, lineNumber, reader.Name);
    }

    private static string? CellToText(object? value) => value switch
    {
        null => null,
        string s => s,
        // O ERP grava Situação/Status como célula booleana do Excel: vira "1"/"0", como no sistema de origem.
        bool b => b ? "1" : "0",
        double d => d.ToString("0.##########", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}

/// <param name="SheetName">Nome da aba (vendas: identifica a loja, ex.: "04 JAGUARIUNA").</param>
internal sealed record SheetData(IReadOnlyList<string> Headers, IReadOnlyList<SheetRow> Rows, int TotalLines, string? SheetName = null)
{
    public bool HasColumn(string column) => Headers.Contains(CatalogText.Key(column));
}

/// <summary>Linha da planilha; <see cref="LineNumber"/> é o número que o usuário vê no Excel.</summary>
internal sealed record SheetRow(int LineNumber, IReadOnlyDictionary<string, string?> Values)
{
    /// <summary>Valor já limpo (sem caracteres de controle nem espaços extras), ou null.</summary>
    public string? Get(string column) =>
        Values.TryGetValue(CatalogText.Key(column), out var value) ? CatalogText.Clean(value) : null;
}
