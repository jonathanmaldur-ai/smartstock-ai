using System.Globalization;

namespace SmartStock.Domain.Analysis;

/// <summary>
/// Alerta de uma análise (Módulo 3.1, decisão 47): só avisa, nunca executa nada (CLAUDE.md).
/// Os alertas de situação (ruptura, excesso, parado...) continuam nas posições; aqui ficam os que dependem
/// de comparar lojas, marcas ou a análise anterior.
/// </summary>
public class StockAlert
{
    public long Id { get; set; }
    public Guid AnalysisId { get; set; }
    public AlertType Type { get; set; }
    public AlertPriority Priority { get; set; }
    public int? ProductId { get; set; }
    public int? StoreId { get; set; }
    public int? BrandId { get; set; }

    /// <summary>Explicação com os números usados.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>"Já vi": mantido nas análises seguintes enquanto o mesmo alerta continuar aparecendo.</summary>
    public DateTimeOffset? SeenAt { get; set; }
    public string? SeenByEmail { get; set; }

    /// <summary>Mesmo alerta em outra análise: mesmo tipo, produto, loja e marca.</summary>
    public string Key => $"{Type}:{ProductId}:{StoreId}:{BrandId}";
}

public enum AlertType
{
    /// <summary>Vende o mínimo do ano e a cobertura acaba antes do crítico (7 dias).</summary>
    PredictedRupture = 1,

    /// <summary>Vende todo dia, o estoque caiu e nada foi recebido desde a análise anterior.</summary>
    SoldWithoutReplenishment = 2,

    /// <summary>Estoque caiu mais da metade desde a análise anterior, sem ser por transferência enviada.</summary>
    SharpStockDrop = 3,

    /// <summary>O mesmo produto sobra numa loja e falta em outra.</summary>
    StoreImbalance = 4,

    /// <summary>Estoque da marca concentrado numa loja que vende pouco dela.</summary>
    BrandConcentration = 5
}

/// <summary>Um produto numa loja, com o que a análise anterior e as transferências contam sobre ele.</summary>
/// <param name="PreviousStock">Estoque na foto da análise anterior; null se não havia análise ou o item não estava nela.</param>
/// <param name="ReceivedUnits">Entradas por transferência na loja entre as duas fotos.</param>
/// <param name="SentUnits">Saídas por transferência da loja entre as duas fotos.</param>
public sealed record AlertPosition(
    int ProductId, int StoreId, int BrandId, bool IsWarehouse,
    decimal Stock, decimal Sold12Months, decimal DailyAverage, decimal? CoverageDays, StockSituation Situation,
    decimal? PreviousStock, decimal ReceivedUnits, decimal SentUnits);

public sealed record AlertCandidate(AlertType Type, AlertPriority Priority, int? ProductId, int? StoreId, int? BrandId, string Message);

/// <summary>Regras dos alertas aprovadas pelo responsável em 28/09/2026 (decisão 47).</summary>
public sealed class AlertRules(StockParameters parameters, Func<int, string> storeName, DateOnly? previousStockDate)
{
    /// <summary>"Vende todo dia": VMD a partir de 1 unidade.</summary>
    public const decimal DailySellerAverage = 1;

    public const decimal SharpDropShare = 0.5m;
    public const decimal SharpDropMinimumUnits = 10;

    /// <summary>Cobertura acima disso numa loja, com falta em outra, é desequilíbrio.</summary>
    public const decimal ImbalanceSurplusDays = 90;

    public const decimal BrandConcentrationShare = 0.6m;
    public const decimal BrandConcentrationMinimumUnits = 100;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public List<AlertCandidate> Evaluate(IReadOnlyCollection<AlertPosition> positions)
    {
        var alerts = new List<AlertCandidate>();
        foreach (var product in positions.GroupBy(p => p.ProductId))
        {
            foreach (var position in product)
                AddPositionAlert(position, alerts);
            AddImbalance(product.Key, product.ToList(), alerts);
        }
        foreach (var brand in positions.Where(p => !p.IsWarehouse).GroupBy(p => p.BrandId))
            AddBrandConcentration(brand.Key, brand.ToList(), alerts);
        return alerts;
    }

    /// <summary>Um alerta por produto na loja, do mais específico ao mais geral.</summary>
    private void AddPositionAlert(AlertPosition p, List<AlertCandidate> alerts)
    {
        if (p.IsWarehouse)
            return;

        var alert = SoldWithoutReplenishment(p) ?? SharpDrop(p) ?? PredictedRupture(p);
        if (alert is not null)
            alerts.Add(alert);
    }

    private AlertCandidate? SoldWithoutReplenishment(AlertPosition p)
    {
        if (p.PreviousStock is not { } before || p.DailyAverage < DailySellerAverage || p.ReceivedUnits > 0 || p.Stock >= before)
            return null;
        if (p.Situation is not (StockSituation.Rupture or StockSituation.CriticalCoverage or StockSituation.BelowMinimum))
            return null;

        return new AlertCandidate(AlertType.SoldWithoutReplenishment, AlertPriority.High, p.ProductId, p.StoreId, null,
            $"Vende {N(p.DailyAverage, 1)} por dia e o estoque caiu de {N(before)} para {N(p.Stock)} un. desde {Since}, " +
            $"sem nenhuma transferência recebida. {CoverageText(p)}");
    }

    /// <summary>Queda não explicada por transferência enviada: mais da metade do estoque e pelo menos 10 unidades.</summary>
    private AlertCandidate? SharpDrop(AlertPosition p)
    {
        if (p.PreviousStock is not { } before || before <= 0)
            return null;
        var unexplained = before - p.Stock - p.SentUnits;
        if (unexplained < SharpDropMinimumUnits || unexplained <= before * SharpDropShare)
            return null;

        var sent = p.SentUnits > 0 ? $" ({N(p.SentUnits)} un. saíram por transferência e não entram na conta)" : string.Empty;
        return new AlertCandidate(AlertType.SharpStockDrop, AlertPriority.Medium, p.ProductId, p.StoreId, null,
            $"Estoque caiu de {N(before)} para {N(p.Stock)} un. desde {Since}{sent}. " +
            $"A venda média é {N(p.DailyAverage, 1)} por dia: confira venda, perda ou lançamento no ERP.");
    }

    private AlertCandidate? PredictedRupture(AlertPosition p)
    {
        if (p.Situation != StockSituation.CriticalCoverage || !IsRelevant(p.Sold12Months))
            return null;
        return new AlertCandidate(AlertType.PredictedRupture, AlertPriority.High, p.ProductId, p.StoreId, null,
            $"{CoverageText(p)} Vende {N(p.DailyAverage, 1)} por dia ({N(p.Sold12Months)} em 12 meses): vai faltar se não for reposto.");
    }

    /// <summary>Um alerta por produto: a loja que mais sobra e a que mais precisa.</summary>
    private void AddImbalance(int productId, List<AlertPosition> stores, List<AlertCandidate> alerts)
    {
        var surplus = stores
            .Where(s => !s.IsWarehouse && s.Stock > 0 && s.CoverageDays > ImbalanceSurplusDays)
            .MaxBy(s => s.CoverageDays);
        var short_ = stores
            .Where(s => !s.IsWarehouse && IsRelevant(s.Sold12Months) &&
                        s.Situation is StockSituation.Rupture or StockSituation.CriticalCoverage or StockSituation.BelowMinimum)
            .MaxBy(s => s.DailyAverage);
        if (surplus is null || short_ is null)
            return;

        var shortage = short_.Situation == StockSituation.Rupture ? "sem estoque" : $"com {N(short_.CoverageDays ?? 0)} dias";
        alerts.Add(new AlertCandidate(AlertType.StoreImbalance, AlertPriority.Medium, productId, short_.StoreId, null,
            $"Sobra em {storeName(surplus.StoreId)} ({N(surplus.Stock)} un., {N(surplus.CoverageDays ?? 0)} dias) e falta em " +
            $"{storeName(short_.StoreId)} ({shortage}, vende {N(short_.DailyAverage, 1)} por dia). Veja as sugestões de transferência."));
    }

    /// <summary>Mais de 60% do estoque da marca (fora o Depósito) numa loja que faz menos da metade dessa fatia nas vendas.</summary>
    private void AddBrandConcentration(int brandId, List<AlertPosition> positions, List<AlertCandidate> alerts)
    {
        var byStore = positions
            .GroupBy(p => p.StoreId)
            .Select(g => (StoreId: g.Key, Stock: g.Sum(p => Math.Max(p.Stock, 0)), Sold: g.Sum(p => p.Sold12Months)))
            .ToList();
        var totalStock = byStore.Sum(s => s.Stock);
        var totalSold = byStore.Sum(s => s.Sold);
        if (totalStock < BrandConcentrationMinimumUnits)
            return;

        var top = byStore.MaxBy(s => s.Stock);
        var stockShare = top.Stock / totalStock;
        var salesShare = totalSold > 0 ? top.Sold / totalSold : 0;
        if (stockShare <= BrandConcentrationShare || salesShare >= stockShare / 2)
            return;

        alerts.Add(new AlertCandidate(AlertType.BrandConcentration, AlertPriority.Low, null, top.StoreId, brandId,
            $"{N(stockShare * 100)}% do estoque da marca nas lojas está em {storeName(top.StoreId)} ({N(top.Stock)} de {N(totalStock)} un.), " +
            $"que faz só {N(salesShare * 100)}% das vendas dela. Vale redistribuir para as lojas que vendem mais."));
    }

    private bool IsRelevant(decimal sold12Months) => sold12Months > 0 && sold12Months >= parameters.MinimumAnnualSales;

    private string Since => previousStockDate is { } date ? date.ToString("dd/MM", PtBr) : "a análise anterior";

    private static string CoverageText(AlertPosition p) => p.Situation == StockSituation.Rupture
        ? "Está sem estoque."
        : $"O estoque dura cerca de {N(p.CoverageDays ?? 0)} dias.";

    private static string N(decimal value, int decimals = 0) => Math.Round(value, decimals).ToString(decimals == 0 ? "#,##0" : "#,##0.#", PtBr);
}

public static class AlertTypeLabels
{
    public static string Of(AlertType type) => type switch
    {
        AlertType.PredictedRupture => "Previsão de ruptura",
        AlertType.SoldWithoutReplenishment => "Vende todo dia sem reposição",
        AlertType.SharpStockDrop => "Queda brusca do estoque",
        AlertType.StoreImbalance => "Sobra numa loja e falta em outra",
        AlertType.BrandConcentration => "Marca mal distribuída",
        _ => type.ToString()
    };
}
