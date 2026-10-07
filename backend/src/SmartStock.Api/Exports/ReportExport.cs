using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using SmartStock.Application.Reports;

namespace SmartStock.Api.Exports;

/// <summary>Excel e CSV de qualquer relatório da central (decisão 42).</summary>
internal static class ReportExport
{
    private const int HeaderRow = 4;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static byte[] Excel(ReportData report)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName(report.Title));

        sheet.Cell(1, 1).Value = report.Title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = Subtitle(report);
        sheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6478");

        for (var c = 0; c < report.Columns.Count; c++)
            sheet.Cell(HeaderRow, c + 1).Value = report.Columns[c].Header;

        for (var r = 0; r < report.Rows.Count; r++)
        {
            for (var c = 0; c < report.Columns.Count; c++)
                WriteCell(sheet.Cell(HeaderRow + 1 + r, c + 1), report.Columns[c].Kind, report.Rows[r][c]);
        }

        var header = sheet.Range(HeaderRow, 1, HeaderRow, report.Columns.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
        sheet.SheetView.FreezeRows(HeaderRow);
        sheet.Range(HeaderRow, 1, HeaderRow + Math.Max(report.Rows.Count, 1), report.Columns.Count).SetAutoFilter();
        sheet.Columns(1, report.Columns.Count).AdjustToContents(HeaderRow, Math.Min(HeaderRow + report.Rows.Count, 600));
        foreach (var column in sheet.Columns(1, report.Columns.Count))
            column.Width = Math.Min(column.Width, 60);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>CSV com ";" e UTF-8 com BOM, para abrir direto no Excel em português; fórmulas neutralizadas.</summary>
    public static byte[] Csv(ReportData report)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', report.Columns.Select(c => Escape(c.Header))));
        foreach (var row in report.Rows)
            builder.AppendLine(string.Join(';', row.Select(Format)));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    public static string FileName(ReportData report, string extension) =>
        $"{report.Key}-{report.StockDate?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd")}.{extension}";

    private static string Subtitle(ReportData report) =>
        $"{report.FilterSummary} · análise de {report.AnalysisDate:dd/MM/yyyy} com o estoque de {report.StockDate:dd/MM/yyyy} · {report.TotalRows:N0} linhas";

    private static void WriteCell(IXLCell cell, ReportColumnKind kind, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "dd/mm/yyyy";
                return;
            case decimal number:
                cell.Value = number;
                cell.Style.NumberFormat.Format = kind == ReportColumnKind.Integer ? "#,##0" : "#,##0.##";
                return;
            case int number:
                cell.Value = number;
                cell.Style.NumberFormat.Format = "#,##0";
                return;
            default:
                // Texto sempre como texto: preserva zeros à esquerda dos códigos (ex.: "0000000001513").
                cell.SetValue(Convert.ToString(value, PtBr) ?? string.Empty);
                return;
        }
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateOnly date => date.ToString("dd/MM/yyyy", PtBr),
        decimal number => number.ToString("0.####", PtBr),
        int number => number.ToString(PtBr),
        _ => Escape(Convert.ToString(value, PtBr) ?? string.Empty)
    };

    private static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0]))
            value = "'" + value;
        return value.IndexOfAny([';', '"', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    /// <summary>Nome de aba do Excel: até 31 caracteres, sem : \ / ? * [ ].</summary>
    private static string SheetName(string title)
    {
        var clean = new string(title.Where(c => !":\\/?*[]".Contains(c)).ToArray());
        return clean.Length <= 31 ? clean : clean[..31];
    }
}
