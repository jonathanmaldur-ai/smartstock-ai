using System.Globalization;
using System.Text.RegularExpressions;
using SmartStock.Domain.Inventory;

namespace SmartStock.Infrastructure.Imports.Definitions;

/// <summary>
/// Relatório de transferências do ERP por documento (decisão 45), como sai do sistema:
/// <code>
/// 0001728300  01/09/2026 - Origem: [05] DEPOSITO - DO RE MI / Destino: [06] MOGI MIRIM 3 - DO RE MI   (tiporeg 1)
///  Usuário: USUARIO1 - Status: Recebido   [Total Compra: R$ 715,68]                                     (tiporeg 1)
///    7901139404185 - SQUISH DUMPLINGS GLITTER | qtde 48                                                (tiporeg 0)
/// </code>
/// Só documentos "Recebido" contam: viram a saída na origem e a entrada no destino, as duas na data do documento
/// (o relatório não traz a data do recebimento). "Pendente" é desconsiderado; status em branco é cancelado.
/// </summary>
internal sealed partial class TransferImportDefinition
{
    private const string DocumentTextColumn = "texto";
    private const string DocumentQuantityColumn = "qtde";
    private const string DocumentRecordTypeColumn = "tiporeg";

    /// <summary>Código do documento: separa este relatório do de vendas detalhado, que também tem texto, qtde e tiporeg.</summary>
    private const string DocumentCodeColumn = "vcod";

    private const string ReceivedStatus = "Recebido";

    [GeneratedRegex(@"^(\d{6,})\s+(\d{2}/\d{2}/\d{4}) - Origem: \[(\d{1,3})\].*/ Destino: \[(\d{1,3})\]")]
    private static partial Regex DocumentLine();

    [GeneratedRegex(@"^Usuário:\s*(.*?)\s+-\s+Status:\s*([^\[]*?)\s*(\[.*)?$")]
    private static partial Regex DocumentStatusLine();

    private static bool IsDocumentLayout(SheetData sheet) =>
        sheet.HasColumn(DocumentTextColumn) && sheet.HasColumn(DocumentQuantityColumn) && sheet.HasColumn(DocumentRecordTypeColumn)
        && sheet.HasColumn(DocumentCodeColumn);

    /// <summary>Lê documento a documento; os itens herdam origem, destino, data, usuário e status do documento.</summary>
    private sealed class DocumentReportParser(StoreLookup stores, Dictionary<string, int> products) : IMovementParser
    {
        private Document? _document;

        public IssueCollector Issues { get; } = new();
        public List<MovementRecord> Movements { get; } = [];

        public void Read(SheetRow row)
        {
            var text = row.Get(DocumentTextColumn);
            if (text is null)
                return;

            if (DocumentLine().Match(text) is { Success: true } document)
                StartDocument(row, document);
            else if (DocumentStatusLine().Match(text) is { Success: true } status)
                ReadStatus(row, status);
            else if (row.Get(DocumentRecordTypeColumn) == "0" && ProductLine().Match(text) is { Success: true } item)
                ReadItem(row, item.Groups[1].Value);
            else
                Issues.Error(row.LineNumber, DocumentTextColumn, "transfer.unknown_line", "Linha não reconhecida no relatório.", text);
        }

        private void StartDocument(SheetRow row, Match line)
        {
            _document = null;
            var number = line.Groups[1].Value;
            var route = $"{line.Groups[3].Value} → {line.Groups[4].Value}";
            if (!DateOnly.TryParseExact(line.Groups[2].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                Issues.Error(row.LineNumber, DocumentTextColumn, "transfer.invalid_date", "Data inválida.", line.Groups[2].Value);
                return;
            }
            var origin = stores.Find(line.Groups[3].Value);
            var destination = stores.Find(line.Groups[4].Value);
            if (origin is null || destination is null)
            {
                Issues.Error(row.LineNumber, DocumentTextColumn, "transfer.store_unknown", "Loja não cadastrada.", route);
                return;
            }
            if (origin.Id == destination.Id)
            {
                Issues.Error(row.LineNumber, DocumentTextColumn, "transfer.same_store", "Origem e destino iguais.", route);
                return;
            }
            if (!origin.IsActive && !destination.IsActive)
            {
                Issues.Warning(row.LineNumber, DocumentTextColumn, "transfer.stores_closed", "Movimentação entre duas lojas fechadas: ignorada.", route);
                return;
            }
            _document = new Document(number, date, origin.Id, destination.Id);
        }

        private void ReadStatus(SheetRow row, Match line)
        {
            if (_document is null)
                return;
            var status = line.Groups[2].Value.Trim();
            _document = _document with { UserName = Truncate(line.Groups[1].Value), Received = status.Equals(ReceivedStatus, StringComparison.OrdinalIgnoreCase) };
            if (_document.Received == true)
                return;

            if (status.Length == 0)
                Issues.Warning(row.LineNumber, DocumentTextColumn, "transfer.document_canceled",
                    "Documento sem status (cancelado): desconsiderado.", _document.Number);
            else
                Issues.Warning(row.LineNumber, DocumentTextColumn, "transfer.document_not_received",
                    "Documento ainda não recebido: desconsiderado.", $"{_document.Number} ({status})");
        }

        private void ReadItem(SheetRow row, string code)
        {
            if (_document is null)
                return; // documento inválido: o motivo já foi registrado na linha dele
            if (_document.Received is null)
            {
                Issues.Error(row.LineNumber, DocumentTextColumn, "transfer.document_without_status", "Item antes da linha de status do documento.", _document.Number);
                return;
            }
            if (_document.Received == false)
                return;
            if (!products.TryGetValue(code, out var productId))
            {
                Issues.Error(row.LineNumber, DocumentTextColumn, "transfer.product_not_found_line",
                    "Produto não cadastrado: importe antes o arquivo de produtos atualizado.", code);
                return;
            }
            var quantity = NumberParser.Parse(row.Get(DocumentQuantityColumn));
            if (quantity is null or <= 0)
            {
                Issues.Error(row.LineNumber, DocumentQuantityColumn, "transfer.invalid_quantity", "Quantidade inválida.", row.Get(DocumentQuantityColumn));
                return;
            }

            // Recebido: saiu da origem e entrou no destino.
            foreach (var direction in (TransferDirection[])[TransferDirection.Out, TransferDirection.In])
                Movements.Add(new MovementRecord(_document.Date, productId, _document.OriginStoreId, _document.DestinationStoreId,
                    direction, false, quantity.Value, _document.UserName, row.LineNumber));
        }

        private sealed record Document(string Number, DateOnly Date, int OriginStoreId, int DestinationStoreId)
        {
            public string? UserName { get; init; }

            /// <summary>null até ler a linha de status.</summary>
            public bool? Received { get; init; }
        }
    }
}
