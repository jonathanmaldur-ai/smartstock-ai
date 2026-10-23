using SmartStock.Application.Common;

namespace SmartStock.Application.Reports;

/// <summary>
/// Vendas em quantidade (decisão 62): as mesmas vendas do dia a dia, só com peças e quantidades, sem nenhum valor em R$.
/// É o relatório de vendas de Gerente, Operador e Consulta; os valores ficam no Vendas por dia, só do Administrador (decisão 59).
/// </summary>
public interface IQuantitySalesReportService
{
    /// <param name="from">Sem período: os últimos 30 dias com venda importada.</param>
    /// <param name="storeId">Sem loja: a rede toda.</param>
    /// <param name="productLimit">Quantos produtos trazer (os que mais venderam em quantidade); null = todos.</param>
    Task<QuantitySalesReport> BuildAsync(
        DateOnly? from, DateOnly? to, int? storeId, int? productLimit = 100, CancellationToken cancellationToken = default);

    /// <summary>Produtos vendidos em cada dia e em cada loja, do dia mais recente para o mais antigo.</summary>
    Task<PagedResult<QuantityProductDay>> ProductDaysAsync(DailySaleProductDayQuery query, CancellationToken cancellationToken = default);
}

/// <param name="Previous">Mesmo número de dias, imediatamente antes; null se não há venda importada nesse período.</param>
public sealed record QuantitySalesReport(
    DateOnly? From,
    DateOnly? To,
    DateOnly? PreviousFrom,
    DateOnly? PreviousTo,
    DateOnly? FirstAvailable,
    DateOnly? LastAvailable,
    string Scope,
    QuantityTotals Current,
    QuantityTotals? Previous,
    IReadOnlyList<QuantityDay> Days,
    IReadOnlyList<QuantityStore> Stores,
    QuantityProducts Products);

/// <param name="Days">Dias com venda no período.</param>
public sealed record QuantityTotals(decimal Pieces, int Days, decimal? PiecesPerDay);

public sealed record QuantityDay(DateOnly Date, decimal Pieces);

public sealed record QuantityStore(int StoreId, string Code, string Name, decimal Pieces, decimal? PreviousPieces, DateOnly? BestDay, decimal? BestDayPieces);

/// <param name="Total">Produtos diferentes vendidos no período (a lista pode trazer só os primeiros).</param>
/// <param name="Available">false quando as vendas do período vieram do relatório resumido, sem os produtos.</param>
public sealed record QuantityProducts(bool Available, int Total, IReadOnlyList<QuantityProduct> Items);

public sealed record QuantityProduct(int ProductId, string Code, string Description, string Brand, decimal Quantity, int Days, int Stores);

public sealed record QuantityProductDay(
    DateOnly Date, int StoreId, string StoreCode, string StoreName, int ProductId, string Code, string Description, string Brand, decimal Quantity);
