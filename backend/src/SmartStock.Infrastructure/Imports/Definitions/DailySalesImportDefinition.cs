using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SmartStock.Domain.Imports;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports.Definitions;

/// <summary>
/// Vendas por dia (decisões 51 e 52): o relatório "Vendas da minha empresa" do ERP, um arquivo por loja.
/// Dois tipos de relatório, reconhecidos pelas colunas:
/// <list type="bullet">
/// <item>Resumido: uma linha por venda ("vennum", "vendta", "ventot", "qtdtot"): grava só o total de cada dia.</item>
/// <item>Detalhado por itens: por venda, um cabeçalho ("tiporeg" 1, com data, valor e "Vr. Bruto" no texto), a linha do
/// vendedor e uma linha por produto ("tiporeg" 2, código no começo do texto): grava o total do dia e os produtos do dia.</item>
/// </list>
/// Nome e código de cliente, vendedor e documento fiscal são lidos e descartados (LGPD). A loja vem da escolha na tela ou
/// do nome do arquivo. Reimportar dias já importados da loja substitui esses dias.
/// </summary>
internal sealed partial class DailySalesImportDefinition(SmartStockDbContext db, TimeProvider clock) : IImportDefinition
{
    private const string SaleColumn = "vennum";
    private const string DateColumn = "vendta";
    private const string SummaryNetColumn = "ventot";
    private const string SummaryGrossColumn = "venbru";
    private const string SummaryItemsColumn = "qtdtot";
    private const string TextColumn = "texto";
    private const string QuantityColumn = "qtde";
    private const string TotalColumn = "vtot";
    private const string UnitPriceColumn = "vuni";
    private const string CounterCustomer = "CLIENTE BALCAO";
    private const string RecordTypeColumn = "tiporeg";
    private const string SaleRecord = "1";
    private const string ItemRecord = "2";

    /// <summary>"03-Aug-26" (como sai do ERP), "2026-08-03" (célula de data) ou "03/08/2026".</summary>
    private static readonly string[] DateFormats = ["dd-MMM-yy", "yyyy-MM-dd", "dd/MM/yyyy"];

    [GeneratedRegex(@"Vr\. Bruto:\s*R\$\s*([\d.,]+)")]
    private static partial Regex GrossValue();

    [GeneratedRegex(@"^(\S+)\s")]
    private static partial Regex ItemCode();

    /// <summary>Cabeçalho: "0154604201  01/07/2026  CLIENTE BALCAO  [Vr. Bruto: ...]".</summary>
    [GeneratedRegex(@"^\S+\s+\d{2}/\d{2}/\d{4}\s+(.*?)\s*\[Vr\. Bruto")]
    private static partial Regex SaleCustomer();

    /// <summary>Linha do vendedor: "Vd: [...]  F.Pagto: [000100]-01-A VISTA  Doc. fiscal: [NF-e / Nº. nota: 007420]".</summary>
    [GeneratedRegex(@"F\.Pagto:\s*\[\d*\]-?(.*?)\s+Doc\. fiscal:\s*\[(.*?)\]")]
    private static partial Regex SaleDetails();

    /// <summary>" / Nº. nota: " ou " / Nº. ordem: " vira " nº ".</summary>
    [GeneratedRegex(@"\s*/\s*N\S*\s*(nota|ordem):\s*")]
    private static partial Regex FiscalNumber();

    public ImportType Type => ImportType.DailySales;

    public string? CheckLayout(SheetData sheet) =>
        IsDetailed(sheet) || IsSummary(sheet)
            ? null
            : "O arquivo não parece ser o relatório de vendas da loja. Colunas esperadas: vennum, vendta e ventot (resumido) " +
              "ou texto, vtot e tiporeg (detalhado por itens).";

    private static bool IsSummary(SheetData sheet) =>
        new[] { SaleColumn, DateColumn, SummaryNetColumn, SummaryItemsColumn }.All(sheet.HasColumn);

    private static bool IsDetailed(SheetData sheet) =>
        new[] { SaleColumn, DateColumn, TextColumn, QuantityColumn, TotalColumn, RecordTypeColumn }.All(sheet.HasColumn);

    public async Task<ImportValidation> ValidateAsync(ImportContext context, SheetData sheet, CancellationToken cancellationToken)
    {
        var stores = await StoreLookup.LoadAsync(db, cancellationToken);
        var fileName = Path.GetFileNameWithoutExtension(context.FileName);
        var store = context.StoreCode is not null
            ? stores.Find(context.StoreCode)
            : stores.Find(fileName) ?? stores.FindByName(fileName) ?? stores.Find(sheet.SheetName) ?? stores.FindByName(sheet.SheetName);
        if (store is null)
            return ImportValidation.Rejected(
                "Não foi possível identificar a loja pelo nome do arquivo (ex.: \"MOGI MIRIM.XLS\"). Escolha a loja no envio.");
        if (!store.IsActive)
            return ImportValidation.Rejected($"A loja {store.Code} {store.Name} está fechada: vendas não são importadas (decisão 4).");

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var reader = IsDetailed(sheet)
            ? new DetailedReader(today, await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, cancellationToken))
            : (SalesReader)new SummaryReader(today);
        foreach (var row in sheet.Rows)
            reader.Read(row);

        var days = reader.Days;
        var scope = days.Count == 0
            ? new ImportScope(StoreId: store.Id)
            : new ImportScope(StoreId: store.Id, PeriodStart: days.Keys.First(), PeriodEnd: days.Keys.Last());
        return new ImportValidation(
            reader.Issues, reader.ValidRows,
            (batchId, ct) => ApplyAsync(store.Id, reader, scope, batchId, ct),
            scope);
    }

    /// <summary>Substitui os dias do arquivo, só desta loja, e grava os totais (e os produtos, no detalhado).</summary>
    private async Task<ApplyResult> ApplyAsync(int storeId, SalesReader reader, ImportScope scope, Guid batchId, CancellationToken cancellationToken)
    {
        if (reader.Days.Count == 0)
            return ApplyResult.None;

        var replaced = await db.DailySales
            .Where(s => s.StoreId == storeId && s.Date >= scope.PeriodStart && s.Date <= scope.PeriodEnd)
            .ExecuteDeleteAsync(cancellationToken);
        if (reader.HasItems)
        {
            await db.DailySaleItems
                .Where(i => i.StoreId == storeId && i.Date >= scope.PeriodStart && i.Date <= scope.PeriodEnd)
                .ExecuteDeleteAsync(cancellationToken);
            await db.StoreSaleLines
                .Where(l => l.StoreId == storeId && l.Date >= scope.PeriodStart && l.Date <= scope.PeriodEnd)
                .ExecuteDeleteAsync(cancellationToken);
            await db.StoreSales
                .Where(s => s.StoreId == storeId && s.Date >= scope.PeriodStart && s.Date <= scope.PeriodEnd)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await PostgresCopy.WriteAsync(db,
            "COPY daily_sales (store_id, date, import_id, sales, pieces, gross, net) FROM STDIN (FORMAT BINARY)",
            reader.Days.ToList(),
            async (writer, day, ct) =>
            {
                await writer.WriteAsync(storeId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(day.Key, NpgsqlDbType.Date, ct);
                await writer.WriteAsync(batchId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(day.Value.Sales, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(day.Value.Pieces, NpgsqlDbType.Numeric, ct);
                await writer.WriteAsync(day.Value.Gross, NpgsqlDbType.Numeric, ct);
                await writer.WriteAsync(day.Value.Net, NpgsqlDbType.Numeric, ct);
            },
            cancellationToken);

        await PostgresCopy.WriteAsync(db,
            "COPY daily_sale_items (store_id, date, product_id, import_id, quantity, amount, sales) FROM STDIN (FORMAT BINARY)",
            reader.Items.ToList(),
            async (writer, item, ct) =>
            {
                await writer.WriteAsync(storeId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(item.Key.Date, NpgsqlDbType.Date, ct);
                await writer.WriteAsync(item.Key.ProductId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(batchId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(item.Value.Quantity, NpgsqlDbType.Numeric, ct);
                await writer.WriteAsync(item.Value.Amount, NpgsqlDbType.Numeric, ct);
                await writer.WriteAsync(item.Value.Sales, NpgsqlDbType.Integer, ct);
            },
            cancellationToken);

        await PostgresCopy.WriteAsync(db,
            "COPY store_sales (store_id, number, date, import_id, gross, net, registered_customer, payment, fiscal_document) FROM STDIN (FORMAT BINARY)",
            reader.Sales,
            async (writer, sale, ct) =>
            {
                await writer.WriteAsync(storeId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(sale.Number, NpgsqlDbType.Varchar, ct);
                await writer.WriteAsync(sale.Date, NpgsqlDbType.Date, ct);
                await writer.WriteAsync(batchId, NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(sale.Gross, NpgsqlDbType.Numeric, ct);
                await writer.WriteAsync(sale.Net, NpgsqlDbType.Numeric, ct);
                await writer.WriteAsync(sale.RegisteredCustomer, NpgsqlDbType.Boolean, ct);
                await WriteTextAsync(writer, sale.Payment, ct);
                await WriteTextAsync(writer, sale.FiscalDocument, ct);
            },
            cancellationToken);

        await PostgresCopy.WriteAsync(db,
            "COPY store_sale_lines (store_id, sale_number, date, product_id, quantity, unit_price, amount) FROM STDIN (FORMAT BINARY)",
            reader.Lines,
            async (writer, line, ct) =>
            {
                await writer.WriteAsync(storeId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(line.SaleNumber, NpgsqlDbType.Varchar, ct);
                await writer.WriteAsync(line.Date, NpgsqlDbType.Date, ct);
                await writer.WriteAsync(line.ProductId, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(line.Quantity, NpgsqlDbType.Numeric, ct);
                await writer.WriteAsync(line.UnitPrice, NpgsqlDbType.Numeric, ct);
                await writer.WriteAsync(line.Amount, NpgsqlDbType.Numeric, ct);
            },
            cancellationToken);
        return new ApplyResult(replaced);
    }

    private static Task WriteTextAsync(Npgsql.NpgsqlBinaryImporter writer, string? value, CancellationToken cancellationToken) =>
        value is null ? writer.WriteNullAsync(cancellationToken) : writer.WriteAsync(value, NpgsqlDbType.Varchar, cancellationToken);

    private sealed record DayTotal(int Sales, decimal Pieces, decimal Gross, decimal Net)
    {
        public static readonly DayTotal Empty = new(0, 0, 0, 0);

        public DayTotal Add(DayTotal other) =>
            new(Sales + other.Sales, Pieces + other.Pieces, Gross + other.Gross, Net + other.Net);
    }

    /// <param name="Sales">Em quantas vendas o produto saiu no dia.</param>
    private sealed record ItemTotal(decimal Quantity, decimal Amount, int Sales);

    /// <summary>Lê as linhas da planilha e acumula por dia (e por produto, no detalhado).</summary>
    private abstract class SalesReader(DateOnly today)
    {
        private readonly HashSet<string> _seenSales = new(StringComparer.Ordinal);

        public IssueCollector Issues { get; } = new();
        public SortedDictionary<DateOnly, DayTotal> Days { get; } = [];
        public Dictionary<(DateOnly Date, int ProductId), ItemTotal> Items { get; } = [];
        public List<SaleRow> Sales { get; } = [];
        public List<LineRow> Lines { get; } = [];
        public int ValidRows { get; protected set; }
        public virtual bool HasItems => false;

        public abstract void Read(SheetRow row);

        protected void AddToDay(DateOnly date, DayTotal total) => Days[date] = (Days.GetValueOrDefault(date) ?? DayTotal.Empty).Add(total);

        /// <summary>Venda repetida no arquivo: contada uma vez.</summary>
        protected bool IsNewSale(SheetRow row)
        {
            var number = row.Get(SaleColumn);
            if (number is null || _seenSales.Add(number))
                return true;
            Issues.Warning(row.LineNumber, SaleColumn, "daily.duplicated", "Venda repetida no arquivo: contada uma vez.", number);
            return false;
        }

        protected bool TryReadDate(SheetRow row, out DateOnly date)
        {
            var raw = row.Get(DateColumn);
            if (!DateOnly.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                Issues.Error(row.LineNumber, DateColumn, "daily.invalid_date", "Data da venda inválida.", raw);
                return false;
            }
            if (date > today)
            {
                Issues.Error(row.LineNumber, DateColumn, "daily.future_date", "Venda com data no futuro.", raw);
                return false;
            }
            return true;
        }

        protected bool TryReadNumbers(SheetRow row, string column, params decimal?[] values)
        {
            if (values.All(v => v is not null))
                return true;
            Issues.Error(row.LineNumber, column, "daily.invalid_value", "Valor ou quantidade inválidos.", row.Get(column));
            return false;
        }
    }

    /// <summary>Resumido: uma linha por venda. "qtdtot" é o número de itens da venda.</summary>
    private sealed class SummaryReader(DateOnly today) : SalesReader(today)
    {
        public override void Read(SheetRow row)
        {
            if (!IsNewSale(row) || !TryReadDate(row, out var date))
                return;
            var net = NumberParser.Parse(row.Get(SummaryNetColumn));
            var items = NumberParser.Parse(row.Get(SummaryItemsColumn));
            var gross = row.Get(SummaryGrossColumn) is null ? net : NumberParser.Parse(row.Get(SummaryGrossColumn));
            if (!TryReadNumbers(row, SummaryNetColumn, net, items, gross))
                return;
            AddToDay(date, new DayTotal(1, items!.Value, gross!.Value, net!.Value));
            ValidRows++;
        }
    }

    /// <summary>Detalhado por itens: cabeçalho da venda, linha do vendedor (ignorada) e uma linha por produto.</summary>
    private sealed class DetailedReader(DateOnly today, Dictionary<string, int> products) : SalesReader(today)
    {
        public override bool HasItems => true;

        public override void Read(SheetRow row)
        {
            switch (row.Get(RecordTypeColumn))
            {
                case SaleRecord when IsSaleHeader(row):
                    ReadSale(row);
                    break;
                case SaleRecord:
                    ReadSaleDetails(row);
                    break;
                case ItemRecord:
                    ReadItem(row);
                    break;
                default:
                    Issues.Error(row.LineNumber, RecordTypeColumn, "daily.unknown_line", "Linha não reconhecida no relatório.", row.Get(TextColumn));
                    break;
            }
        }

        /// <summary>O cabeçalho começa pelo número da venda; a linha do vendedor começa por "Vd:".</summary>
        private static bool IsSaleHeader(SheetRow row) =>
            row.Get(SaleColumn) is { } number && row.Get(TextColumn)?.StartsWith(number, StringComparison.Ordinal) == true;

        private SaleRow? _sale;

        private void ReadSale(SheetRow row)
        {
            _sale = null;
            if (!IsNewSale(row) || !TryReadDate(row, out var date))
                return;
            var text = row.Get(TextColumn) ?? string.Empty;
            var net = NumberParser.Parse(row.Get(TotalColumn));
            var gross = GrossValue().Match(text) is { Success: true } m ? NumberParser.Parse(m.Groups[1].Value) : net;
            if (!TryReadNumbers(row, TotalColumn, net, gross))
                return;
            AddToDay(date, new DayTotal(1, 0, gross!.Value, net!.Value));
            ValidRows++;

            // Só se o cliente é o de balcão ou identificado: o nome não é guardado (LGPD).
            var registered = SaleCustomer().Match(text) is { Success: true } c && !c.Groups[1].Value.Trim().Equals(CounterCustomer, StringComparison.OrdinalIgnoreCase);
            _sale = new SaleRow(row.Get(SaleColumn)!, date, gross.Value, net.Value, registered);
            Sales.Add(_sale);
        }

        /// <summary>Linha do vendedor: guarda só a forma de pagamento e o documento fiscal da venda.</summary>
        private void ReadSaleDetails(SheetRow row)
        {
            ValidRows++;
            if (_sale is null || SaleDetails().Match(row.Get(TextColumn) ?? string.Empty) is not { Success: true } m)
                return;
            _sale.Payment = Truncate(m.Groups[1].Value.Trim(), 60);
            _sale.FiscalDocument = Truncate(FiscalNumber().Replace(m.Groups[2].Value.Trim(), " nº "), 80);
        }

        /// <summary>As peças do dia são a soma das quantidades dos itens, inclusive de produto fora do cadastro.</summary>
        private void ReadItem(SheetRow row)
        {
            if (!TryReadDate(row, out var date))
                return;
            var quantity = NumberParser.Parse(row.Get(QuantityColumn));
            var unitPrice = NumberParser.Parse(row.Get(UnitPriceColumn));
            var amount = NumberParser.Parse(row.Get(TotalColumn));
            if (!TryReadNumbers(row, QuantityColumn, quantity, amount, unitPrice))
                return;
            AddToDay(date, new DayTotal(0, quantity!.Value, 0, 0));
            ValidRows++;

            var code = ItemCode().Match(row.Get(TextColumn) ?? string.Empty) is { Success: true } m ? m.Groups[1].Value : null;
            if (code is null || !products.TryGetValue(code, out var productId))
            {
                Issues.Warning(row.LineNumber, TextColumn, "daily.product_not_found",
                    "Produto fora do cadastro: entra no total do dia, mas não na lista de produtos vendidos.", code);
                return;
            }

            var saleNumber = row.Get(SaleColumn);
            var key = (date, productId);
            var current = Items.GetValueOrDefault(key) ?? new ItemTotal(0, 0, 0);
            var newSale = saleNumber is not null && _salesPerItem.Add((date, productId, saleNumber));
            Items[key] = new ItemTotal(current.Quantity + quantity.Value, current.Amount + amount!.Value, current.Sales + (newSale ? 1 : 0));
            if (_sale is not null && _sale.Number == saleNumber)
                Lines.Add(new LineRow(_sale.Number, date, productId, quantity.Value, unitPrice!.Value, amount.Value));
        }

        private readonly HashSet<(DateOnly, int, string)> _salesPerItem = [];

        private static string? Truncate(string value, int max) => value.Length == 0 ? null : value.Length > max ? value[..max] : value;
    }

    /// <summary>Venda lida do cabeçalho; pagamento e documento vêm da linha seguinte.</summary>
    private sealed class SaleRow(string number, DateOnly date, decimal gross, decimal net, bool registeredCustomer)
    {
        public string Number { get; } = number;
        public DateOnly Date { get; } = date;
        public decimal Gross { get; } = gross;
        public decimal Net { get; } = net;
        public bool RegisteredCustomer { get; } = registeredCustomer;
        public string? Payment { get; set; }
        public string? FiscalDocument { get; set; }
    }

    private sealed record LineRow(string SaleNumber, DateOnly Date, int ProductId, decimal Quantity, decimal UnitPrice, decimal Amount);
}
