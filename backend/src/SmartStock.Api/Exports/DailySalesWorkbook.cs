using ClosedXML.Excel;
using SmartStock.Application.Reports;
using static SmartStock.Api.Exports.ExcelLayout;

namespace SmartStock.Api.Exports;

/// <summary>Excel das vendas por dia (decisão 51): resumo, dia a dia, lojas e dia da semana.</summary>
internal static class DailySalesWorkbook
{
    private const string Money = "#,##0.00";
    private const string Number = "#,##0";

    public static byte[] Build(DailySalesReport report, IReadOnlyList<DailySaleProductDay> productDays, IReadOnlyList<SaleLineExport> saleLines)
    {
        using var workbook = new XLWorkbook();
        var period = report.From is null
            ? $"{report.Scope} · sem vendas por dia importadas"
            : $"{report.Scope} · {report.From:dd/MM/yyyy} a {report.To:dd/MM/yyyy}";

        Summary(workbook.AddWorksheet("Resumo"), report, period);
        Days(workbook.AddWorksheet("Por dia"), report, period);
        Stores(workbook.AddWorksheet("Por loja"), report, period);
        Weekdays(workbook.AddWorksheet("Dia da semana"), report, period);
        if (report.Products.Available)
            Products(workbook.AddWorksheet("Produtos"), report, period);
        if (productDays.Count > 0)
            ProductDays(workbook.AddWorksheet("Produtos por dia"), productDays, period);
        if (saleLines.Count > 0)
            SaleLines(workbook.AddWorksheet("Itens das vendas"), saleLines, period);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Summary(IXLWorksheet sheet, DailySalesReport report, string period)
    {
        Title(sheet, "Vendas por dia · resumo", period);
        Header(sheet, 4, "Indicador", "Período", "Período anterior");
        (string Name, decimal? Now, decimal? Before, string Format)[] lines =
        [
            ("Valor vendido (R$)", report.Current.Net, report.Previous?.Net, Money),
            ("Vendas (cupons)", report.Current.Sales, report.Previous?.Sales, Number),
            ("Peças", report.Current.Pieces, report.Previous?.Pieces, Number),
            ("Ticket médio (R$)", report.Current.AverageTicket, report.Previous?.AverageTicket, Money),
            ("Peças por venda", report.Current.PiecesPerSale, report.Previous?.PiecesPerSale, "#,##0.00"),
            ("Média por dia (R$)", report.Current.NetPerDay, report.Previous?.NetPerDay, Money),
            ("Dias com venda", report.Current.Days, report.Previous?.Days, Number)
        ];
        var row = 5;
        foreach (var (name, now, before, format) in lines)
        {
            sheet.Cell(row, 1).Value = name;
            if (now is not null) sheet.Cell(row, 2).Value = now.Value;
            if (before is not null) sheet.Cell(row, 3).Value = before.Value;
            sheet.Range(row, 2, row, 3).Style.NumberFormat.Format = format;
            row++;
        }
        if (report.PreviousFrom is not null)
            sheet.Cell(row + 1, 1).Value = $"Período anterior: {report.PreviousFrom:dd/MM/yyyy} a {report.PreviousTo:dd/MM/yyyy}";
        sheet.Columns().AdjustToContents();
    }

    private static void Days(IXLWorksheet sheet, DailySalesReport report, string period)
    {
        Title(sheet, "Vendas por dia · dia a dia", period);
        Header(sheet, 4, "Data", "Dia da semana", "Valor vendido (R$)", "Vendas", "Peças", "Ticket médio (R$)");
        var row = 5;
        foreach (var d in report.Days)
        {
            sheet.Cell(row, 1).Value = d.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Cell(row, 2).Value = report.Weekdays[(int)d.Date.DayOfWeek].Label;
            sheet.Cell(row, 3).Value = d.Net;
            sheet.Cell(row, 4).Value = d.Sales;
            sheet.Cell(row, 5).Value = d.Pieces;
            if (d.Sales > 0) sheet.Cell(row, 6).Value = Math.Round(d.Net / d.Sales, 2);
            sheet.Cell(row, 3).Style.NumberFormat.Format = Money;
            sheet.Range(row, 4, row, 5).Style.NumberFormat.Format = Number;
            sheet.Cell(row, 6).Style.NumberFormat.Format = Money;
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.Columns().AdjustToContents();
    }

    private static void Stores(IXLWorksheet sheet, DailySalesReport report, string period)
    {
        Title(sheet, "Vendas por dia · por loja", period);
        Header(sheet, 4, "Loja", "Valor vendido (R$)", "Período anterior (R$)", "Vendas", "Peças", "Ticket médio (R$)",
            "Dias com venda", "Melhor dia", "Valor do melhor dia (R$)");
        var row = 5;
        foreach (var s in report.Stores)
        {
            sheet.Cell(row, 1).Value = $"{s.Code} {s.Name}";
            sheet.Cell(row, 2).Value = s.Net;
            if (s.PreviousNet is not null) sheet.Cell(row, 3).Value = s.PreviousNet.Value;
            sheet.Cell(row, 4).Value = s.Sales;
            sheet.Cell(row, 5).Value = s.Pieces;
            if (s.AverageTicket is not null) sheet.Cell(row, 6).Value = s.AverageTicket.Value;
            sheet.Cell(row, 7).Value = s.Days;
            if (s.BestDay is not null)
            {
                sheet.Cell(row, 8).Value = s.BestDay.Value.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(row, 8).Style.DateFormat.Format = "dd/MM/yyyy";
            }
            if (s.BestDayNet is not null) sheet.Cell(row, 9).Value = s.BestDayNet.Value;
            sheet.Range(row, 2, row, 3).Style.NumberFormat.Format = Money;
            sheet.Range(row, 4, row, 5).Style.NumberFormat.Format = Number;
            sheet.Cell(row, 6).Style.NumberFormat.Format = Money;
            sheet.Cell(row, 9).Style.NumberFormat.Format = Money;
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.Columns().AdjustToContents();
    }

    private static void Products(IXLWorksheet sheet, DailySalesReport report, string period)
    {
        Title(sheet, "Vendas por dia · produtos vendidos", $"{period} · {report.Products.Total:N0} produtos, do que mais vendeu em valor");
        Header(sheet, 4, "Código", "Produto", "Marca", "Quantidade", "Valor (R$)", "Dias com venda", "Lojas");
        var row = 5;
        foreach (var p in report.Products.Items)
        {
            sheet.Cell(row, 1).Value = p.Code;
            sheet.Cell(row, 2).Value = p.Description;
            sheet.Cell(row, 3).Value = p.Brand;
            sheet.Cell(row, 4).Value = p.Quantity;
            sheet.Cell(row, 5).Value = p.Amount;
            sheet.Cell(row, 6).Value = p.Days;
            sheet.Cell(row, 7).Value = p.Stores;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "#,##0.##";
            sheet.Cell(row, 5).Style.NumberFormat.Format = Money;
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.Column(1).Style.NumberFormat.Format = "@";
        sheet.Columns().AdjustToContents();
    }

    /// <summary>Uma linha por dia, loja e produto: o que cada loja vendeu em cada dia.</summary>
    private static void ProductDays(IXLWorksheet sheet, IReadOnlyList<DailySaleProductDay> rows, string period)
    {
        Title(sheet, "Vendas por dia · produtos por dia e loja", period);
        Header(sheet, 4, "Data", "Loja", "Código", "Produto", "Marca", "Quantidade", "Valor (R$)", "Vendas", "Preço médio (R$)");
        var row = 5;
        foreach (var r in rows)
        {
            sheet.Cell(row, 1).Value = r.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 2).Value = $"{r.StoreCode} {r.StoreName}";
            sheet.Cell(row, 3).Value = r.Code;
            sheet.Cell(row, 4).Value = r.Description;
            sheet.Cell(row, 5).Value = r.Brand;
            sheet.Cell(row, 6).Value = r.Quantity;
            sheet.Cell(row, 7).Value = r.Amount;
            sheet.Cell(row, 8).Value = r.Sales;
            if (r.Quantity != 0) sheet.Cell(row, 9).Value = Math.Round(r.Amount / r.Quantity, 2);
            row++;
        }
        if (row > 5)
        {
            sheet.Range(5, 1, row - 1, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Range(5, 6, row - 1, 6).Style.NumberFormat.Format = "#,##0.##";
            sheet.Range(5, 7, row - 1, 7).Style.NumberFormat.Format = Money;
            sheet.Range(5, 9, row - 1, 9).Style.NumberFormat.Format = Money;
            sheet.Range(4, 1, row - 1, 9).SetAutoFilter();
        }
        sheet.Column(3).Style.NumberFormat.Format = "@";
        sheet.SheetView.FreezeRows(4);
        sheet.Columns(1, 9).AdjustToContents(4, 200);
    }

    /// <summary>Cada produto de cada venda (decisão 53): sem nome de cliente nem vendedor.</summary>
    private static void SaleLines(IXLWorksheet sheet, IReadOnlyList<SaleLineExport> rows, string period)
    {
        Title(sheet, "Vendas por dia · itens de cada venda", period);
        Header(sheet, 4, "Data", "Loja", "Venda", "Cliente", "Documento", "Pagamento", "Total da venda (R$)", "Código", "Produto",
            "Quantidade", "Preço unitário (R$)", "Valor (R$)");
        var row = 5;
        foreach (var r in rows)
        {
            sheet.Cell(row, 1).Value = r.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 2).Value = $"{r.StoreCode} {r.StoreName}";
            sheet.Cell(row, 3).Value = r.SaleNumber;
            sheet.Cell(row, 4).Value = r.RegisteredCustomer ? "Cadastrado" : "Balcão";
            sheet.Cell(row, 5).Value = r.FiscalDocument;
            sheet.Cell(row, 6).Value = r.Payment;
            sheet.Cell(row, 7).Value = r.SaleTotal;
            sheet.Cell(row, 8).Value = r.Code;
            sheet.Cell(row, 9).Value = r.Description;
            sheet.Cell(row, 10).Value = r.Quantity;
            sheet.Cell(row, 11).Value = r.UnitPrice;
            sheet.Cell(row, 12).Value = r.Amount;
            row++;
        }
        if (row > 5)
        {
            sheet.Range(5, 1, row - 1, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Range(5, 7, row - 1, 7).Style.NumberFormat.Format = Money;
            sheet.Range(5, 10, row - 1, 10).Style.NumberFormat.Format = "#,##0.##";
            sheet.Range(5, 11, row - 1, 12).Style.NumberFormat.Format = Money;
            sheet.Range(4, 1, row - 1, 12).SetAutoFilter();
        }
        sheet.Column(3).Style.NumberFormat.Format = "@";
        sheet.Column(8).Style.NumberFormat.Format = "@";
        sheet.SheetView.FreezeRows(4);
        sheet.Columns(1, 12).AdjustToContents(4, 200);
    }

    private static void Weekdays(IXLWorksheet sheet, DailySalesReport report, string period)
    {
        Title(sheet, "Vendas por dia · média por dia da semana", period);
        Header(sheet, 4, "Dia da semana", "Média vendida (R$)", "Média de vendas", "Dias no período");
        var row = 5;
        foreach (var w in report.Weekdays)
        {
            sheet.Cell(row, 1).Value = w.Label;
            sheet.Cell(row, 2).Value = w.AverageNet;
            sheet.Cell(row, 3).Value = w.AverageSales;
            sheet.Cell(row, 4).Value = w.Days;
            sheet.Cell(row, 2).Style.NumberFormat.Format = Money;
            sheet.Cell(row, 3).Style.NumberFormat.Format = "#,##0.0";
            row++;
        }
        sheet.Columns().AdjustToContents();
    }
}
