using SmartStock.Application.Common;

namespace SmartStock.Application.Reports;

/// <summary>Vendas por dia e por loja (decisão 51), na central de relatórios.</summary>
public interface IDailySalesReportService
{
    /// <param name="from">Sem período: os últimos 30 dias com venda importada.</param>
    /// <param name="storeId">Sem loja: a rede toda.</param>
    /// <param name="productLimit">Quantos produtos trazer (os que mais venderam em valor); null = todos.</param>
    Task<DailySalesReport> BuildAsync(
        DateOnly? from, DateOnly? to, int? storeId, int? productLimit = 100, CancellationToken cancellationToken = default);

    /// <summary>Produtos vendidos em cada dia e em cada loja (decisão 52), do dia mais recente para o mais antigo.</summary>
    Task<PagedResult<DailySaleProductDay>> ProductDaysAsync(DailySaleProductDayQuery query, CancellationToken cancellationToken = default);

    /// <summary>As vendas em que um produto saiu no período, numa loja ou na rede toda (decisão 53).</summary>
    Task<IReadOnlyList<SaleLineDetail>> SaleLinesAsync(
        int? storeId, DateOnly from, DateOnly to, int productId, CancellationToken cancellationToken = default);

    /// <summary>Todos os itens das vendas do período (Excel).</summary>
    Task<IReadOnlyList<SaleLineExport>> AllSaleLinesAsync(DateOnly? from, DateOnly? to, int? storeId, CancellationToken cancellationToken = default);
}

/// <param name="Previous">Mesmo número de dias, imediatamente antes; null se não há venda importada nesse período.</param>
/// <param name="FirstAvailable">Primeiro e último dia com venda importada (para o seletor de período).</param>
public sealed record DailySalesReport(
    DateOnly? From,
    DateOnly? To,
    DateOnly? PreviousFrom,
    DateOnly? PreviousTo,
    DateOnly? FirstAvailable,
    DateOnly? LastAvailable,
    string Scope,
    DailySalesTotals Current,
    DailySalesTotals? Previous,
    IReadOnlyList<DailySalesDay> Days,
    IReadOnlyList<DailySalesStore> Stores,
    IReadOnlyList<DailySalesWeekday> Weekdays,
    DailySalesProducts Products);

/// <param name="Days">Dias com venda no período.</param>
public sealed record DailySalesTotals(
    decimal Net, decimal Gross, int Sales, decimal Pieces, int Days, decimal? AverageTicket, decimal? PiecesPerSale, decimal? NetPerDay);

public sealed record DailySalesDay(DateOnly Date, decimal Net, int Sales, decimal Pieces);

public sealed record DailySalesStore(
    int StoreId, string Code, string Name, decimal Net, int Sales, decimal Pieces, int Days,
    decimal? AverageTicket, decimal? PreviousNet, DateOnly? BestDay, decimal? BestDayNet);

/// <param name="Total">Produtos diferentes vendidos no período (a lista pode trazer só os primeiros).</param>
/// <param name="Available">false quando as vendas do período vieram do relatório resumido, sem os produtos.</param>
public sealed record DailySalesProducts(bool Available, int Total, IReadOnlyList<DailySalesProduct> Items);

public sealed record DailySalesProduct(
    int ProductId, string Code, string Description, string Brand, decimal Quantity, decimal Amount, int Days, int Stores);

/// <param name="PageSize">null = todas as linhas (Excel).</param>
public sealed record DailySaleProductDayQuery(
    DateOnly? From, DateOnly? To, int? StoreId, string? Search, int Page = 1, int? PageSize = 50);

/// <param name="Sales">Em quantas vendas o produto saiu (preço médio = Amount ÷ Quantity).</param>
public sealed record DailySaleProductDay(
    DateOnly Date, string StoreCode, string StoreName, int ProductId, string Code, string Description, string Brand, decimal Quantity, decimal Amount,
    int Sales, int StoreId);

/// <param name="SaleItems">Quantos produtos diferentes a venda teve.</param>
/// <param name="RegisteredCustomer">false = cliente de balcão; o nome do cliente não é guardado.</param>
public sealed record SaleLineDetail(
    DateOnly Date, string StoreCode, string StoreName, string SaleNumber, decimal Quantity, decimal UnitPrice, decimal Amount, decimal SaleTotal, int SaleItems,
    bool RegisteredCustomer, string? Payment, string? FiscalDocument);

public sealed record SaleLineExport(
    DateOnly Date, string StoreCode, string StoreName, string SaleNumber, bool RegisteredCustomer, string? FiscalDocument, string? Payment,
    decimal SaleTotal, string Code, string Description, decimal Quantity, decimal UnitPrice, decimal Amount);

/// <param name="DayOfWeek">0 = domingo … 6 = sábado.</param>
/// <param name="AverageNet">Média do valor vendido nos dias desse dia da semana que tiveram venda.</param>
public sealed record DailySalesWeekday(int DayOfWeek, string Label, decimal AverageNet, decimal AverageSales, int Days);
