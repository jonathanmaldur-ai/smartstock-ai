using ClosedXML.Excel;
using SmartStock.Application.Reports;
using static SmartStock.Api.Exports.ExcelLayout;

namespace SmartStock.Api.Exports;

/// <summary>Excel das vendas em quantidade (decisão 62): só peças e quantidades, sem valores em R$.</summary>
internal static class QuantitySalesWorkbook
{
    private const string Number = "#,##0.##";

    public static byte[] Build(QuantitySalesReport report, IReadOnlyList<QuantityProductDay> productDays)
    {
        using var workbook = new XLWorkbook();
        var period = report.From is null
            ? $"{report.Scope} · sem vendas por dia importadas"
            : $"{report.Scope} · {report.From:dd/MM/yyyy} a {report.To:dd/MM/yyyy}";

        Summary(workbook.AddWorksheet("Resumo"), report, period);
        Days(workbook.AddWorksheet("Por dia"), report, period);
        Stores(workbook.AddWorksheet("Por loja"), report, period);
        if (report.Products.Available)
            Products(workbook.AddWorksheet("Produtos"), report, period);
        if (productDays.Count > 0)
            ProductDays(workbook.AddWorksheet("Produtos por dia"), productDays, period);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Summary(IXLWorksheet sheet, QuantitySalesReport report, string period)
    {
        Title(sheet, "Vendas em quantidade · resumo", period);
        Header(sheet, 4, "Indicador", "Período", "Período anterior");
        (string Name, decimal? Now, decimal? Before)[] lines =
        [
            ("Peças vendidas", report.Current.Pieces, report.Previous?.Pieces),
            ("Média de peças por dia", report.Current.PiecesPerDay, report.Previous?.PiecesPerDay),
            ("Dias com venda", report.Current.Days, report.Previous?.Days)
        ];
        var row = 5;
        foreach (var (name, now, before) in lines)
        {
            sheet.Cell(row, 1).Value = name;
            if (now is not null) sheet.Cell(row, 2).Value = now.Value;
            if (before is not null) sheet.Cell(row, 3).Value = before.Value;
            sheet.Range(row, 2, row, 3).Style.NumberFormat.Format = Number;
            row++;
        }
        if (report.PreviousFrom is not null)
            sheet.Cell(row + 1, 1).Value = $"Período anterior: {report.PreviousFrom:dd/MM/yyyy} a {report.PreviousTo:dd/MM/yyyy}";
        sheet.Columns().AdjustToContents();
    }

    private static void Days(IXLWorksheet sheet, QuantitySalesReport report, string period)
    {
        Title(sheet, "Vendas em quantidade · dia a dia", period);
        Header(sheet, 4, "Data", "Peças vendidas");
        var row = 5;
        foreach (var d in report.Days)
        {
            sheet.Cell(row, 1).Value = d.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Cell(row, 2).Value = d.Pieces;
            sheet.Cell(row, 2).Style.NumberFormat.Format = Number;
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.Columns().AdjustToContents();
    }

    private static void Stores(IXLWorksheet sheet, QuantitySalesReport report, string period)
    {
        Title(sheet, "Vendas em quantidade · por loja", period);
        Header(sheet, 4, "Loja", "Peças vendidas", "Período anterior", "Melhor dia", "Peças no melhor dia");
        var row = 5;
        foreach (var s in report.Stores)
        {
            sheet.Cell(row, 1).Value = $"{s.Code} {s.Name}";
            sheet.Cell(row, 2).Value = s.Pieces;
            if (s.PreviousPieces is not null) sheet.Cell(row, 3).Value = s.PreviousPieces.Value;
            if (s.BestDay is not null)
            {
                sheet.Cell(row, 4).Value = s.BestDay.Value.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(row, 4).Style.DateFormat.Format = "dd/MM/yyyy";
            }
            if (s.BestDayPieces is not null) sheet.Cell(row, 5).Value = s.BestDayPieces.Value;
            sheet.Range(row, 2, row, 3).Style.NumberFormat.Format = Number;
            sheet.Cell(row, 5).Style.NumberFormat.Format = Number;
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.Columns().AdjustToContents();
    }

    private static void Products(IXLWorksheet sheet, QuantitySalesReport report, string period)
    {
        Title(sheet, "Vendas em quantidade · produtos", $"{period} · {report.Products.Total:N0} produtos, do que mais vendeu em quantidade");
        Header(sheet, 4, "Código", "Produto", "Marca", "Quantidade", "Dias com venda", "Lojas");
        var row = 5;
        foreach (var p in report.Products.Items)
        {
            sheet.Cell(row, 1).Value = p.Code;
            sheet.Cell(row, 2).Value = p.Description;
            sheet.Cell(row, 3).Value = p.Brand;
            sheet.Cell(row, 4).Value = p.Quantity;
            sheet.Cell(row, 5).Value = p.Days;
            sheet.Cell(row, 6).Value = p.Stores;
            sheet.Cell(row, 4).Style.NumberFormat.Format = Number;
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.Column(1).Style.NumberFormat.Format = "@";
        sheet.Columns().AdjustToContents();
    }

    /// <summary>Uma linha por dia, loja e produto: o que cada loja vendeu em cada dia.</summary>
    private static void ProductDays(IXLWorksheet sheet, IReadOnlyList<QuantityProductDay> rows, string period)
    {
        Title(sheet, "Vendas em quantidade · produtos por dia e loja", period);
        Header(sheet, 4, "Data", "Loja", "Código", "Produto", "Marca", "Quantidade");
        var row = 5;
        foreach (var r in rows)
        {
            sheet.Cell(row, 1).Value = r.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Cell(row, 2).Value = $"{r.StoreCode} {r.StoreName}";
            sheet.Cell(row, 3).Value = r.Code;
            sheet.Cell(row, 4).Value = r.Description;
            sheet.Cell(row, 5).Value = r.Brand;
            sheet.Cell(row, 6).Value = r.Quantity;
            sheet.Cell(row, 6).Style.NumberFormat.Format = Number;
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.Column(3).Style.NumberFormat.Format = "@";
        sheet.Range(4, 1, row - 1, 6).SetAutoFilter();
        sheet.Columns().AdjustToContents();
    }
}
