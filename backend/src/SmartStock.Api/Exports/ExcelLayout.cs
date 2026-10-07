using ClosedXML.Excel;

namespace SmartStock.Api.Exports;

/// <summary>Título, subtítulo e cabeçalho no padrão dos relatórios em Excel.</summary>
internal static class ExcelLayout
{
    public static void Title(IXLWorksheet sheet, string title, string subtitle)
    {
        sheet.Cell(1, 1).Value = title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = subtitle;
        sheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#5A6478");
    }

    public static void Header(IXLWorksheet sheet, int row, params string[] headers)
    {
        for (var c = 0; c < headers.Length; c++)
            sheet.Cell(row, c + 1).Value = headers[c];
        var range = sheet.Range(row, 1, row, headers.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
    }
}
