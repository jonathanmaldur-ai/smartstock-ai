namespace SmartStock.Infrastructure.Reports;

/// <summary>Período dos relatórios de vendas do dia a dia (Vendas por dia e Vendas em quantidade).</summary>
internal static class SalesPeriod
{
    private const int DefaultDays = 30;
    private const int MaxDays = 400;

    /// <summary>
    /// Sem datas, os últimos 30 dias. Depois do último dia importado não há venda: o período termina nele,
    /// senão a comparação com o período anterior (que tem o mesmo número de dias) mostraria quedas que não existem.
    /// </summary>
    public static (DateOnly Start, DateOnly End) Resolve(DateOnly? from, DateOnly? to, DateOnly last)
    {
        var end = to is { } requested && requested < last ? requested : last;
        var start = from ?? end.AddDays(-(DefaultDays - 1));
        if (start > end) start = end;
        if (end.DayNumber - start.DayNumber + 1 > MaxDays) start = end.AddDays(-(MaxDays - 1));
        return (start, end);
    }

    /// <summary>O período anterior, com o mesmo número de dias, imediatamente antes.</summary>
    public static (DateOnly Start, DateOnly End) Previous(DateOnly start, DateOnly end)
    {
        var length = end.DayNumber - start.DayNumber + 1;
        var previousEnd = start.AddDays(-1);
        return (previousEnd.AddDays(-(length - 1)), previousEnd);
    }
}
