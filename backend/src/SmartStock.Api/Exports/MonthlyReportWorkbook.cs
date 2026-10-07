using ClosedXML.Excel;
using SmartStock.Application.Reports;
using static SmartStock.Api.Exports.ExcelLayout;

namespace SmartStock.Api.Exports;

/// <summary>Excel do relatório mensal (decisão 42): uma aba para os indicadores, uma para as lojas e uma para as transferências.</summary>
internal static class MonthlyReportWorkbook
{
    private const string Number = "#,##0";

    public static byte[] Build(MonthlyReport report)
    {
        using var workbook = new XLWorkbook();
        var period = report.Previous is null
            ? $"Estoque de {report.Current.StockDate:dd/MM/yyyy} (sem análise anterior para comparar)"
            : $"Estoque de {report.Previous.StockDate:dd/MM/yyyy} → {report.Current.StockDate:dd/MM/yyyy}";

        Indicators(workbook.AddWorksheet("Resumo"), report, period);
        Stores(workbook.AddWorksheet("Por loja"), report, period);
        Transfers(workbook.AddWorksheet("Transferências"), report.Transfers, period);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Indicators(IXLWorksheet sheet, MonthlyReport report, string period)
    {
        Title(sheet, "Relatório mensal · indicadores", period);
        Header(sheet, 4, "Indicador", "Anterior", "Atual", "Variação", "Unidade");
        var row = 5;
        foreach (var i in report.Indicators)
        {
            sheet.Cell(row, 1).Value = i.Name;
            if (i.Previous is not null) sheet.Cell(row, 2).Value = i.Previous.Value;
            sheet.Cell(row, 3).Value = i.Current;
            if (i.Previous is not null) sheet.Cell(row, 4).Value = i.Current - i.Previous.Value;
            sheet.Cell(row, 5).Value = i.Unit;
            sheet.Range(row, 2, row, 4).Style.NumberFormat.Format = Number;
            row++;
        }
        sheet.Columns().AdjustToContents();
    }

    private static void Stores(IXLWorksheet sheet, MonthlyReport report, string period)
    {
        Title(sheet, "Relatório mensal · por loja", period);
        Header(sheet, 4, "Loja",
            "Rupturas (antes)", "Rupturas (agora)", "Negativos (antes)", "Negativos (agora)",
            "Excesso (antes)", "Excesso (agora)", "Parados (antes)", "Parados (agora)",
            "Estoque (antes)", "Estoque (agora)", "Cobertura dias (antes)", "Cobertura dias (agora)");
        var row = 5;
        foreach (var s in report.Stores)
        {
            sheet.Cell(row, 1).Value = $"{s.Code} {s.Name}";
            Pair(sheet, row, 2, s.Previous?.RelevantRuptures, s.Current.RelevantRuptures);
            Pair(sheet, row, 4, s.Previous?.NegativeItems, s.Current.NegativeItems);
            Pair(sheet, row, 6, s.Previous?.Excess, s.Current.Excess);
            Pair(sheet, row, 8, s.Previous?.Stagnant, s.Current.Stagnant);
            Pair(sheet, row, 10, s.Previous?.StockUnits, s.Current.StockUnits);
            Pair(sheet, row, 12, s.Previous?.CoverageDays, s.Current.CoverageDays);
            sheet.Range(row, 2, row, 13).Style.NumberFormat.Format = Number;
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.Columns().AdjustToContents();
    }

    private static void Transfers(IXLWorksheet sheet, TransferPerformance t, string period)
    {
        Title(sheet, "Relatório mensal · transferências", t.Since is null ? "Todas as decisões registradas" : $"Decididas desde {t.Since.Value.ToLocalTime():dd/MM/yyyy HH:mm} · {period}");
        Header(sheet, 4, "Indicador", "Valor");
        (string Name, decimal? Value)[] lines =
        [
            ("Sugestões aprovadas", t.Approved), ("Unidades aprovadas", t.ApprovedUnits),
            ("Aprovadas já realizadas (apareceram no arquivo do ERP)", t.Completed), ("Unidades realizadas", t.CompletedUnits),
            ("Rejeitadas", t.Rejected), ("Pendentes de aprovação agora", t.PendingApproval),
            ("Precisão (% das aprovadas que foram realizadas)", t.PrecisionPercent), ("Dias, em média, até realizar", t.AverageDaysToComplete)
        ];
        var row = 5;
        foreach (var (name, value) in lines)
        {
            sheet.Cell(row, 1).Value = name;
            if (value is not null) sheet.Cell(row, 2).Value = value.Value;
            sheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0.#";
            row++;
        }
        sheet.Columns().AdjustToContents();
    }

    private static void Pair(IXLWorksheet sheet, int row, int column, decimal? before, decimal? now)
    {
        if (before is not null) sheet.Cell(row, column).Value = before.Value;
        if (now is not null) sheet.Cell(row, column + 1).Value = now.Value;
    }
}
