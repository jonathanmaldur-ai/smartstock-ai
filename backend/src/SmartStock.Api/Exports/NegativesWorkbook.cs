using ClosedXML.Excel;
using SmartStock.Application.Analysis;
using SmartStock.Domain.Analysis;

namespace SmartStock.Api.Exports;

/// <summary>Relatório de estoque negativo para correção no ERP (decisão 26). O SmartStock não corrige o estoque.</summary>
internal static class NegativesWorkbook
{
    private static readonly string[] Headers =
    [
        "Prioridade", "Loja", "Código", "Produto", "Referência", "Marca", "Categoria", "Estoque", "Venda 12 meses",
        "Causas prováveis", "Transferido sem entrada (un.)", "Possível duplicado (código)", "Corrigido?"
    ];

    public static byte[] Build(IReadOnlyList<NegativeItemDto> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Negativos");
        for (var c = 0; c < Headers.Length; c++)
            sheet.Cell(1, c + 1).Value = Headers[c];

        for (var r = 0; r < items.Count; r++)
        {
            var i = items[r];
            var row = sheet.Row(r + 2);
            var c = 1;
            row.Cell(c++).Value = PriorityLabel(i.Priority);
            row.Cell(c++).Value = $"{i.StoreCode} {i.StoreName}";
            row.Cell(c++).SetValue(i.ProductCode);
            row.Cell(c++).Value = i.ProductDescription;
            row.Cell(c++).SetValue(i.ProductReference ?? string.Empty);
            row.Cell(c++).Value = i.BrandName;
            row.Cell(c++).Value = i.CategoryName;
            row.Cell(c++).Value = i.Quantity;
            row.Cell(c++).Value = i.Sold12Months;
            row.Cell(c++).Value = string.Join("; ", i.Causes.Select(CauseLabel));
            row.Cell(c++).Value = i.PendingTransferUnits == 0 ? Blank.Value : i.PendingTransferUnits;
            row.Cell(c++).SetValue(i.DuplicateProductCode ?? string.Empty);
            row.Cell(c).Value = string.Empty;
        }

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(items.Count + 1, 1), Headers.Length).SetAutoFilter();
        sheet.Columns().AdjustToContents(1, Math.Min(items.Count + 1, 500));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static string CauseLabel(NegativeCause cause) => cause switch
    {
        NegativeCause.TransferNotReceived => "Transferência enviada e não recebida",
        NegativeCause.SaleWithoutEntry => "Vende sem entrada registrada",
        NegativeCause.ShippedWithoutEntry => "Saiu por transferência sem entrada de nota",
        NegativeCause.FractionalUnit => "Unidade fracionada",
        NegativeCause.PossibleDuplicate => "Possível cadastro duplicado",
        _ => cause.ToString()
    };

    private static string PriorityLabel(AlertPriority priority) => priority switch
    {
        AlertPriority.Critical => "Crítica",
        AlertPriority.High => "Alta",
        AlertPriority.Medium => "Média",
        AlertPriority.Low => "Baixa",
        _ => "—"
    };
}
