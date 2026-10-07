using ClosedXML.Excel;
using SmartStock.Application.Analysis;
using SmartStock.Domain.Analysis;

namespace SmartStock.Api.Exports;

/// <summary>Planilha das sugestões de transferência (decisão 11: download em Excel), para a equipe separar a mercadoria.</summary>
internal static class SuggestionsWorkbook
{
    private static readonly string[] Headers =
    [
        "Prioridade", "Status", "Origem", "Destino", "Código", "Produto", "Referência", "Marca", "Categoria", "Quantidade",
        "Estoque origem", "VMD origem", "Cobertura origem (dias)", "Estoque destino", "VMD destino", "Cobertura destino (dias)",
        "Cobertura após (dias)", "Conferir estoque do destino", "Motivo", "Análise de", "Decidido por", "Decidido em", "Observação", "Realizada em", "Qtd. realizada"
    ];

    public static byte[] Build(IReadOnlyList<SuggestionDto> suggestions)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sugestões");
        for (var c = 0; c < Headers.Length; c++)
            sheet.Cell(1, c + 1).Value = Headers[c];

        for (var r = 0; r < suggestions.Count; r++)
            WriteRow(sheet.Row(r + 2), suggestions[r]);

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(suggestions.Count + 1, 1), Headers.Length).SetAutoFilter();
        sheet.Columns(1, Headers.Length - 5).AdjustToContents(1, Math.Min(suggestions.Count + 1, 500));
        sheet.Column(19).Width = 80;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteRow(IXLRow row, SuggestionDto s)
    {
        var c = 1;
        row.Cell(c++).Value = PriorityLabel(s.Priority);
        row.Cell(c++).Value = StatusLabel(s.Status);
        row.Cell(c++).Value = $"{s.OriginCode} {s.OriginName}";
        row.Cell(c++).Value = $"{s.DestinationCode} {s.DestinationName}";
        // Código como texto: preserva zeros à esquerda (ex.: "0000000001513").
        row.Cell(c++).SetValue(s.ProductCode);
        row.Cell(c++).Value = s.ProductDescription;
        row.Cell(c++).SetValue(s.ProductReference ?? string.Empty);
        row.Cell(c++).Value = s.BrandName;
        row.Cell(c++).Value = s.CategoryName;
        row.Cell(c++).Value = s.Quantity;
        row.Cell(c++).Value = s.OriginStock;
        row.Cell(c++).Value = Math.Round(s.OriginDailyAverage, 2);
        row.Cell(c++).Value = s.OriginCoverageDays is null ? Blank.Value : Math.Round(s.OriginCoverageDays.Value, 0);
        row.Cell(c++).Value = s.DestinationStock;
        row.Cell(c++).Value = Math.Round(s.DestinationDailyAverage, 2);
        row.Cell(c++).Value = s.DestinationCoverageDays is null ? Blank.Value : Math.Round(s.DestinationCoverageDays.Value, 0);
        row.Cell(c++).Value = s.DestinationCoverageAfter is null ? Blank.Value : Math.Round(s.DestinationCoverageAfter.Value, 0);
        row.Cell(c++).Value = s.DestinationNegative ? "Sim: estoque negativo" : string.Empty;
        row.Cell(c++).Value = s.Reason;
        row.Cell(c++).Value = s.AnalysisDate.ToDateTime(TimeOnly.MinValue);
        row.Cell(c - 1).Style.DateFormat.Format = "dd/mm/yyyy";
        row.Cell(c++).Value = s.DecidedByEmail ?? string.Empty;
        row.Cell(c++).Value = s.DecidedAt is null ? Blank.Value : s.DecidedAt.Value.ToLocalTime().DateTime;
        row.Cell(c - 1).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
        row.Cell(c++).Value = s.DecisionNote ?? string.Empty;
        row.Cell(c).Value = s.CompletedOn is null ? Blank.Value : s.CompletedOn.Value.ToDateTime(TimeOnly.MinValue);
        row.Cell(c++).Style.DateFormat.Format = "dd/mm/yyyy";
        row.Cell(c).Value = s.CompletedQuantity is null ? Blank.Value : s.CompletedQuantity.Value;
    }

    private static string PriorityLabel(AlertPriority priority) => priority switch
    {
        AlertPriority.Critical => "Crítica",
        AlertPriority.High => "Alta",
        AlertPriority.Medium => "Média",
        AlertPriority.Low => "Baixa",
        _ => "—"
    };

    private static string StatusLabel(SuggestionStatus status) => status switch
    {
        SuggestionStatus.Suggested => "Pendente",
        SuggestionStatus.Approved => "Aprovada",
        SuggestionStatus.Rejected => "Rejeitada",
        SuggestionStatus.Completed => "Realizada",
        SuggestionStatus.Superseded => "Substituída",
        _ => status.ToString()
    };
}
