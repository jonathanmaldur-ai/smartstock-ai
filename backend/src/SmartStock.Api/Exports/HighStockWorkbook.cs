using ClosedXML.Excel;
using SmartStock.Application.Reports;

namespace SmartStock.Api.Exports;

/// <summary>
/// Excel do relatório de estoque alto (decisão 64): Resumo, uma aba por grupo (Casa e Decor, Brinquedos, Outras) com
/// todas as lojas em colunas, "Conferir estoque" com os produtos de número suspeito e uma aba por loja.
/// </summary>
internal static class HighStockWorkbook
{
    private const int HeaderRow = 4;
    private const string QuantityFormat = "#,##0;[Red]-#,##0;\"-\"";
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#E8EEF7");
    private static readonly XLColor SubtitleColor = XLColor.FromHtml("#5A6478");

    public static byte[] Build(ReportData report)
    {
        var fixedCount = HighStockReport.FixedColumns.Count;
        var stores = report.Columns.Skip(fixedCount).Select(c => c.Header).ToList();
        var rows = report.Rows;

        using var workbook = new XLWorkbook();
        AddSummary(workbook, report);
        foreach (var group in HighStockReport.Groups)
            AddGroupSheet(workbook, report, group, rows.Where(r => GroupOf(r) == group).ToList());
        AddCheckSheet(workbook, report);
        for (var s = 0; s < stores.Count; s++)
            AddStoreSheet(workbook, report, stores[s], fixedCount + s);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddSummary(XLWorkbook workbook, ReportData report)
    {
        var sheet = workbook.AddWorksheet("Resumo");
        WriteTitle(sheet, report);
        string[] headers = ["Grupo", "Produtos", "Unidades"];
        for (var c = 0; c < headers.Length; c++)
            sheet.Cell(HeaderRow, c + 1).Value = headers[c];
        StyleHeader(sheet.Range(HeaderRow, 1, HeaderRow, headers.Length));

        var row = HeaderRow + 1;
        foreach (var group in HighStockReport.Groups)
        {
            var items = report.Rows.Where(r => GroupOf(r) == group).ToList();
            sheet.Cell(row, 1).Value = group;
            sheet.Cell(row, 2).Value = items.Count;
            sheet.Cell(row, 3).Value = items.Sum(Total);
            row++;
        }

        sheet.Cell(row, 1).Value = "Conferir estoque no ERP";
        sheet.Cell(row, 2).Value = report.Rows.Count(r => r[HighStockReport.CheckColumn] is not null);
        sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#B45309");
        row++;

        row++;
        sheet.Cell(row, 1).Value = $"{HighStockReport.OtherGroup}: por categoria";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;
        var byCategory = report.Rows
            .Where(r => GroupOf(r) == HighStockReport.OtherGroup)
            .GroupBy(r => r[HighStockReport.CategoryColumn] as string ?? string.Empty)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key);
        foreach (var category in byCategory)
        {
            sheet.Cell(row, 1).Value = category.Key;
            sheet.Cell(row, 2).Value = category.Count();
            sheet.Cell(row, 3).Value = category.Sum(Total);
            row++;
        }

        sheet.Column(2).Style.NumberFormat.Format = "#,##0";
        sheet.Column(3).Style.NumberFormat.Format = "#,##0";
        sheet.Column(1).Width = 34;
        sheet.Columns(2, 3).Width = 14;
    }

    /// <summary>Um produto por linha, sem a coluna Grupo (é o nome da aba).</summary>
    private static void AddGroupSheet(XLWorkbook workbook, ReportData report, string group, List<IReadOnlyList<object?>> rows)
    {
        var sheet = workbook.AddWorksheet(group);
        WriteTitle(sheet, report);
        var columns = report.Columns.Skip(1).ToList();
        WriteTable(sheet, columns, rows.Select(r => r.Skip(1).ToList()).ToList());
        sheet.SheetView.Freeze(HeaderRow, 2);
    }

    /// <summary>Produtos com negativos altos ou cobertura acima de 5 anos: o número pode estar errado no ERP.</summary>
    private static void AddCheckSheet(XLWorkbook workbook, ReportData report)
    {
        var sheet = workbook.AddWorksheet("Conferir estoque");
        WriteTitle(sheet, report);
        sheet.Cell(3, 1).Value =
            "Negativos altos: o negativo passa de 10% do total (ex.: transferência lançada errada). Cobertura acima de 5 anos: quantidade fora da realidade da venda.";
        sheet.Cell(3, 1).Style.Font.FontColor = SubtitleColor;
        var rows = report.Rows.Where(r => r[HighStockReport.CheckColumn] is not null).Select(r => r.ToList()).ToList();
        WriteTable(sheet, report.Columns.ToList(), rows);
        sheet.SheetView.Freeze(HeaderRow, 3);
    }

    /// <summary>Os produtos da lista que a loja tem em estoque, por grupo e da maior quantidade para a menor.</summary>
    private static void AddStoreSheet(XLWorkbook workbook, ReportData report, string store, int storeColumn)
    {
        var sheet = workbook.AddWorksheet(SheetName(store));
        WriteTitle(sheet, report);
        var columns = report.Columns.Take(HighStockReport.FixedColumns.Count).ToList();
        columns.Insert(HighStockReport.TotalColumn, new ReportColumn("Qtd na loja", ReportColumnKind.Decimal));
        columns[HighStockReport.TotalColumn + 1] = new ReportColumn("Total na rede", ReportColumnKind.Decimal);
        columns.Insert(HighStockReport.TotalColumn + 2, new ReportColumn("% da rede", ReportColumnKind.Decimal));

        var groupOrder = HighStockReport.Groups.ToList();
        var rows = report.Rows
            .Where(r => Quantity(r[storeColumn]) > 0)
            .OrderBy(r => groupOrder.IndexOf(GroupOf(r)))
            .ThenByDescending(r => Quantity(r[storeColumn]))
            .Select(r =>
            {
                var values = r.Take(HighStockReport.FixedColumns.Count).ToList();
                var inStore = Quantity(r[storeColumn]);
                values.Insert(HighStockReport.TotalColumn, inStore);
                values.Insert(HighStockReport.TotalColumn + 2, Total(r) > 0 ? Math.Round(inStore / Total(r), 4) : null);
                return values;
            })
            .ToList();
        WriteTable(sheet, columns, rows);
        sheet.Column(HighStockReport.TotalColumn + 3).Style.NumberFormat.Format = "0%";
        sheet.SheetView.FreezeRows(HeaderRow);
    }

    private static void WriteTable(IXLWorksheet sheet, List<ReportColumn> columns, List<List<object?>> rows)
    {
        for (var c = 0; c < columns.Count; c++)
            sheet.Cell(HeaderRow, c + 1).Value = columns[c].Header;
        for (var r = 0; r < rows.Count; r++)
        for (var c = 0; c < columns.Count; c++)
            WriteCell(sheet.Cell(HeaderRow + 1 + r, c + 1), rows[r][c]);

        StyleHeader(sheet.Range(HeaderRow, 1, HeaderRow, columns.Count));
        sheet.Range(HeaderRow, 1, HeaderRow + Math.Max(rows.Count, 1), columns.Count).SetAutoFilter();
        for (var c = 0; c < columns.Count; c++)
        {
            if (columns[c].Kind == ReportColumnKind.Decimal)
                sheet.Column(c + 1).Style.NumberFormat.Format = QuantityFormat;
        }
        sheet.Columns(1, columns.Count).AdjustToContents(HeaderRow, Math.Min(HeaderRow + rows.Count, 600));
        foreach (var column in sheet.Columns(1, columns.Count))
            column.Width = Math.Min(column.Width, 50);
    }

    private static void WriteCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case decimal number:
                cell.Value = number;
                return;
            default:
                // Texto sempre como texto: preserva zeros à esquerda dos códigos.
                cell.SetValue(Convert.ToString(value) ?? string.Empty);
                return;
        }
    }

    private static void WriteTitle(IXLWorksheet sheet, ReportData report)
    {
        sheet.Cell(1, 1).Value = report.Title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value =
            $"{report.FilterSummary} · estoque de {report.StockDate:dd/MM/yyyy} · estoque negativo (em vermelho) não entra no total";
        sheet.Cell(2, 1).Style.Font.FontColor = SubtitleColor;
    }

    private static void StyleHeader(IXLRange header)
    {
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = HeaderFill;
    }

    private static string GroupOf(IReadOnlyList<object?> row) => row[HighStockReport.GroupColumn] as string ?? HighStockReport.OtherGroup;

    private static decimal Total(IReadOnlyList<object?> row) => Quantity(row[HighStockReport.TotalColumn]);

    private static decimal Quantity(object? value) => value as decimal? ?? 0;

    /// <summary>Nome de aba do Excel: até 31 caracteres, sem : \ / ? * [ ].</summary>
    private static string SheetName(string title)
    {
        var clean = new string(title.Where(c => !":\\/?*[]".Contains(c)).ToArray());
        return clean.Length <= 31 ? clean : clean[..31];
    }
}
