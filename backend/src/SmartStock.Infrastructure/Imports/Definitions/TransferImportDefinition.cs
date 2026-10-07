using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SmartStock.Domain.Imports;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports.Definitions;

/// <summary>
/// Relatório de transferências do ERP (decisões 28 e 33). Em blocos por produto:
/// <code>
/// 7897653539482 - NOME DO PRODUTO
///      13/03/2026 - TRANSFERENCIA DE 14 PARA 06 | entrada | saída | usuário
///      30/03/2026 - CANC. TRANSF. DE 01 PARA 11 | ...
/// TOTAL: | entradas | saídas
/// </code>
/// Dois layouts, reconhecidos pelo cabeçalho: quantidades nas colunas ENTRADA/SAIDA, ou nas colunas "mpent"/"mpsai".
/// Também aceita o relatório por documento (TransferImportDefinition.Documents.cs, decisão 45).
/// Reimportar datas já importadas substitui as movimentações dessas datas.
/// </summary>
internal sealed partial class TransferImportDefinition(SmartStockDbContext db) : IImportDefinition
{
    /// <summary>Tratado à mão ou como sai do ERP ("texto").</summary>
    private static readonly string[] DescriptionColumns = ["DESCRIÇÃO DA TRANSFERENCIA", "TRANSFERENCIA DESCRIÇÃO", "DESCRIÇÃO TRANSFERENCIA", "texto"];
    private const string InColumn = "ENTRADA";
    private const string OutColumn = "SAIDA";
    private const string AlternativeInColumn = "mpent";
    private const string AlternativeOutColumn = "mpsai";
    private static readonly string[] UserColumns = ["USUARIO", "usu"];
    private const int MaxUserNameLength = 100;

    [GeneratedRegex(@"^(\d{2}/\d{2}/\d{4}) - (TRANSFERENCIA|CANC\. TRANSF\.) DE (\d{1,3}) PARA (\d{1,3})$")]
    private static partial Regex MovementLine();

    [GeneratedRegex(@"^(\S+) - ")]
    private static partial Regex ProductLine();

    public ImportType Type => ImportType.Transfers;

    public string? CheckLayout(SheetData sheet)
    {
        if (IsDocumentLayout(sheet))
            return null;
        if (FindDescriptionColumn(sheet) is null)
            return "O arquivo não parece ser o relatório de transferências: coluna de descrição da transferência não encontrada.";
        var layout = FindQuantityColumns(sheet);
        return layout is null
            ? "O arquivo não parece ser o relatório de transferências. Colunas não encontradas: ENTRADA e SAIDA (ou mpent e mpsai)."
            : null;
    }

    public async Task<ImportValidation> ValidateAsync(ImportContext context, SheetData sheet, CancellationToken cancellationToken)
    {
        var stores = await StoreLookup.LoadAsync(db, cancellationToken);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, cancellationToken);
        IMovementParser parser = IsDocumentLayout(sheet)
            ? new DocumentReportParser(stores, products)
            : new ReportParser(FindDescriptionColumn(sheet)!, FindQuantityColumns(sheet)!.Value, stores, products);

        foreach (var row in sheet.Rows)
            parser.Read(row);

        var movements = parser.Movements;
        var scope = movements.Count == 0
            ? ImportScope.None
            : new ImportScope(PeriodStart: movements.Min(m => m.Date), PeriodEnd: movements.Max(m => m.Date));

        return new ImportValidation(
            parser.Issues,
            movements.Select(m => m.SourceRow).Distinct().Count(), // linhas: no relatório por documento, cada item vira saída e entrada
            (batchId, ct) => ApplyAsync(movements, scope, batchId, ct),
            scope);
    }

    private static string? FindDescriptionColumn(SheetData sheet) =>
        DescriptionColumns.FirstOrDefault(sheet.HasColumn);

    private static (string In, string Out)? FindQuantityColumns(SheetData sheet)
    {
        if (sheet.HasColumn(AlternativeInColumn) && sheet.HasColumn(AlternativeOutColumn))
            return (AlternativeInColumn, AlternativeOutColumn);
        if (sheet.HasColumn(InColumn) && sheet.HasColumn(OutColumn))
            return (InColumn, OutColumn);
        return null;
    }

    /// <summary>Substitui as movimentações do período do arquivo (decisão 33) e grava as novas.</summary>
    private async Task<ApplyResult> ApplyAsync(List<MovementRecord> movements, ImportScope scope, Guid batchId, CancellationToken cancellationToken)
    {
        if (movements.Count == 0)
            return ApplyResult.None;

        var replaced = await db.TransferMovements
            .Where(m => m.Date >= scope.PeriodStart && m.Date <= scope.PeriodEnd)
            .ExecuteDeleteAsync(cancellationToken);

        await PostgresCopy.WriteAsync(db,
            "COPY transfer_movements (import_id, date, product_id, origin_store_id, destination_store_id, direction, " +
            "is_cancellation, quantity, user_name, source_row) FROM STDIN (FORMAT BINARY)",
            movements,
            async (writer, m, ct) =>
            {
                await writer.WriteAsync(batchId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(m.Date, NpgsqlDbType.Date, ct);
                await writer.WriteAsync(m.ProductId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(m.OriginStoreId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(m.DestinationStoreId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(m.Direction.ToString(), NpgsqlDbType.Varchar, ct);
                await writer.WriteAsync(m.IsCancellation, NpgsqlDbType.Boolean, ct);
                await writer.WriteAsync(m.Quantity, NpgsqlDbType.Numeric, ct);
                if (m.UserName is null)
                    await writer.WriteNullAsync(ct);
                else
                    await writer.WriteAsync(m.UserName, NpgsqlDbType.Varchar, ct);
                await writer.WriteAsync(m.SourceRow, NpgsqlDbType.Integer, ct);
            },
            cancellationToken);

        return new ApplyResult(replaced);
    }

    private sealed record MovementRecord(
        DateOnly Date, int ProductId, int OriginStoreId, int DestinationStoreId,
        TransferDirection Direction, bool IsCancellation, decimal Quantity, string? UserName, int SourceRow);

    /// <summary>Lê o relatório linha a linha, lembrando o produto do bloco atual e somando para conferir o TOTAL.</summary>
    private sealed class ReportParser(
        string descriptionColumn,
        (string In, string Out) quantityColumns,
        StoreLookup stores,
        Dictionary<string, int> products) : IMovementParser
    {
        private string? _productCode;
        private int? _productId;
        private decimal _sumIn;
        private decimal _sumOut;

        public IssueCollector Issues { get; } = new();
        public List<MovementRecord> Movements { get; } = [];

        public void Read(SheetRow row)
        {
            var description = row.Get(descriptionColumn);
            if (description is null)
                return;

            var movement = MovementLine().Match(description);
            if (movement.Success)
                ReadMovement(row, movement);
            else if (description.StartsWith("TOTAL:", StringComparison.OrdinalIgnoreCase))
                CheckTotal(row);
            else if (ProductLine().Match(description) is { Success: true } product)
                StartProduct(row, product.Groups[1].Value);
            else
                Issues.Error(row.LineNumber, descriptionColumn, "transfer.unknown_line", "Linha não reconhecida no relatório.", description);
        }

        private void StartProduct(SheetRow row, string code)
        {
            _productCode = code;
            _sumIn = _sumOut = 0;
            _productId = products.TryGetValue(code, out var id) ? id : null;
            if (_productId is null)
                Issues.Error(row.LineNumber, descriptionColumn, "transfer.product_not_found",
                    "Produto não cadastrado: as movimentações dele não serão gravadas. Importe antes o arquivo de produtos atualizado.", code);
        }

        private void ReadMovement(SheetRow row, Match movement)
        {
            var (quantityIn, quantityOut) = ReadQuantities(row);
            _sumIn += quantityIn ?? 0;
            _sumOut += quantityOut ?? 0;

            if (_productCode is null)
            {
                Issues.Error(row.LineNumber, descriptionColumn, "transfer.no_product", "Movimentação fora do bloco de um produto.");
                return;
            }
            if (_productId is null)
            {
                Issues.Error(row.LineNumber, descriptionColumn, "transfer.product_not_found_line", "Movimentação de produto não cadastrado.", _productCode);
                return;
            }

            var origin = stores.Find(movement.Groups[3].Value);
            var destination = stores.Find(movement.Groups[4].Value);
            if (!DateOnly.TryParseExact(movement.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                Issues.Error(row.LineNumber, descriptionColumn, "transfer.invalid_date", "Data inválida.", movement.Groups[1].Value);
                return;
            }
            if (!ValidateStores(row, movement, origin, destination) || !ValidateQuantities(row, quantityIn, quantityOut))
                return;

            var isIn = quantityIn > 0;
            Movements.Add(new MovementRecord(
                date,
                _productId.Value,
                origin!.Id,
                destination!.Id,
                isIn ? TransferDirection.In : TransferDirection.Out,
                movement.Groups[2].Value.StartsWith("CANC", StringComparison.Ordinal),
                isIn ? quantityIn!.Value : quantityOut!.Value,
                Truncate(UserColumns.Select(row.Get).FirstOrDefault(v => v is not null)),
                row.LineNumber));
        }

        private (decimal? In, decimal? Out) ReadQuantities(SheetRow row) =>
            (NumberParser.Parse(row.Get(quantityColumns.In)), NumberParser.Parse(row.Get(quantityColumns.Out)));

        private bool ValidateStores(SheetRow row, Match movement, Domain.Catalog.Store? origin, Domain.Catalog.Store? destination)
        {
            var route = $"{movement.Groups[3].Value} → {movement.Groups[4].Value}";
            if (origin is null || destination is null)
            {
                Issues.Error(row.LineNumber, descriptionColumn, "transfer.store_unknown", "Loja não cadastrada.", route);
                return false;
            }
            // Decisão 33: movimentação entre loja fechada e ativa é gravada (ex.: estoque da loja fechada redistribuído);
            // só é ignorada quando as duas pontas estão fechadas.
            if (!origin.IsActive && !destination.IsActive)
            {
                Issues.Warning(row.LineNumber, descriptionColumn, "transfer.stores_closed",
                    "Movimentação entre duas lojas fechadas: ignorada.", route);
                return false;
            }
            if (origin.Id == destination.Id)
            {
                Issues.Error(row.LineNumber, descriptionColumn, "transfer.same_store", "Origem e destino iguais.", route);
                return false;
            }
            return true;
        }

        private bool ValidateQuantities(SheetRow row, decimal? quantityIn, decimal? quantityOut)
        {
            if (quantityIn is null || quantityOut is null || quantityIn < 0 || quantityOut < 0)
            {
                Issues.Error(row.LineNumber, quantityColumns.In, "transfer.invalid_quantity", "Quantidade inválida.",
                    $"{row.Get(quantityColumns.In)} / {row.Get(quantityColumns.Out)}");
                return false;
            }
            if (quantityIn > 0 && quantityOut > 0)
            {
                Issues.Error(row.LineNumber, quantityColumns.In, "transfer.in_and_out", "Linha com entrada e saída ao mesmo tempo.",
                    $"{quantityIn:0.####} / {quantityOut:0.####}");
                return false;
            }
            if (quantityIn == 0 && quantityOut == 0)
            {
                Issues.Warning(row.LineNumber, quantityColumns.In, "transfer.zero_quantity", "Movimentação sem quantidade: ignorada.");
                return false;
            }
            return true;
        }

        /// <summary>Conferência de integridade: o TOTAL do bloco deve bater com a soma das movimentações.</summary>
        private void CheckTotal(SheetRow row)
        {
            var (totalIn, totalOut) = ReadQuantities(row);
            if (_productCode is not null && (totalIn != _sumIn || totalOut != _sumOut))
                Issues.Warning(row.LineNumber, descriptionColumn, "transfer.total_mismatch",
                    "O TOTAL do produto não bate com a soma das movimentações. Confira se o arquivo está completo.",
                    $"{_productCode}: {totalIn:0.####}/{totalOut:0.####} ≠ {_sumIn:0.####}/{_sumOut:0.####}");
            _productCode = null;
            _productId = null;
        }

    }

    private interface IMovementParser
    {
        IssueCollector Issues { get; }
        List<MovementRecord> Movements { get; }
        void Read(SheetRow row);
    }

    private static string? Truncate(string? value) =>
        value is { Length: > MaxUserNameLength } ? value[..MaxUserNameLength] : value;
}
