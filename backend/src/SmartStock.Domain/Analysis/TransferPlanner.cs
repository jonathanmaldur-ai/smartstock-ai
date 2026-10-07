using System.Globalization;

namespace SmartStock.Domain.Analysis;

/// <summary>Estoque e venda de um produto numa unidade, para o planejamento.</summary>
public sealed record StoreStock(int StoreId, string StoreLabel, bool IsWarehouse, decimal Stock, decimal Sold12Months);

/// <summary>Transferência aprovada e ainda não realizada: a mercadoria já está "a caminho".</summary>
public sealed record InTransit(int OriginStoreId, int DestinationStoreId, decimal Quantity);

/// <param name="OriginStock">Estoque importado da origem (o que a tela mostra; a projeção fica em <see cref="Origin"/>).</param>
public sealed record PlannedTransfer(
    int OriginStoreId,
    int DestinationStoreId,
    decimal Quantity,
    AlertPriority Priority,
    string Reason,
    PositionMetrics Origin,
    PositionMetrics Destination,
    decimal? DestinationCoverageAfter,
    bool DestinationNegative,
    decimal OriginStock,
    decimal DestinationStock);

/// <param name="Stock">Estoque importado do destino.</param>
public sealed record PlannedPurchase(int StoreId, decimal Quantity, PositionMetrics Destination, decimal Stock);

public sealed record TransferPlan(IReadOnlyList<PlannedTransfer> Transfers, IReadOnlyList<PlannedPurchase> Purchases);

/// <summary>
/// Regras de sugestão de transferência da seção 2 (decisão 8), com a venda mínima da decisão 35:
/// 1. Destino: loja que vende (≥ venda mínima) com cobertura abaixo do mínimo.
/// 2. Quantidade: leva o destino até o estoque ideal.
/// 3. Origem: cede só o que passa do estoque ideal dela; o Depósito (não vende) pode ceder tudo.
/// 4. Prioridade de origem: Depósito, depois a loja com maior cobertura.
/// 5. Loja com estoque negativo nunca é origem (decisão 26).
/// O que nenhuma origem cobre vira sugestão de compra (decisão 10).
/// </summary>
public sealed class TransferPlanner(StockRules rules)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public TransferPlan Plan(IReadOnlyList<StoreStock> stores, IReadOnlyList<InTransit> inTransit, int daysSinceStock)
    {
        var p = rules.Parameters;
        var metrics = stores.ToDictionary(s => s.StoreId, s => rules.Evaluate(s.IsWarehouse, s.Stock, s.Sold12Months, daysSinceStock));

        // Estoque efetivo já considerando o que foi aprovado e ainda está a caminho.
        var effective = stores.ToDictionary(s => s.StoreId, s => metrics[s.StoreId].EffectiveStock);
        foreach (var t in inTransit)
        {
            if (effective.ContainsKey(t.DestinationStoreId)) effective[t.DestinationStoreId] += t.Quantity;
            if (effective.ContainsKey(t.OriginStoreId)) effective[t.OriginStoreId] = Math.Max(effective[t.OriginStoreId] - t.Quantity, 0);
        }

        var available = stores.ToDictionary(s => s.StoreId, s => Available(s, metrics[s.StoreId], effective[s.StoreId]));

        var destinations = stores
            .Where(s => !s.IsWarehouse && rules.IsRelevant(s.Sold12Months))
            .Where(s => effective[s.StoreId] < p.MinimumDays * metrics[s.StoreId].DailyAverage)
            .OrderBy(s => effective[s.StoreId] / metrics[s.StoreId].DailyAverage)
            .ThenByDescending(s => s.Sold12Months)
            .ToList();

        var transfers = new List<PlannedTransfer>();
        var purchases = new List<PlannedPurchase>();
        foreach (var destination in destinations)
        {
            var target = metrics[destination.StoreId];
            var missing = Math.Ceiling(p.IdealDays * target.DailyAverage - effective[destination.StoreId]);
            if (missing < 1)
                continue;

            foreach (var origin in OrderOrigins(stores, destination, metrics, available))
            {
                var quantity = Math.Min(missing, available[origin.StoreId]);
                available[origin.StoreId] -= quantity;
                missing -= quantity;
                effective[destination.StoreId] += quantity;
                transfers.Add(BuildTransfer(origin, destination, quantity, metrics, effective[destination.StoreId]));
                if (missing < 1)
                    break;
            }

            if (missing >= 1)
                purchases.Add(new PlannedPurchase(destination.StoreId, missing, target, destination.Stock));
        }

        return new TransferPlan(transfers, purchases);
    }

    /// <summary>Quanto a unidade pode ceder, em unidades inteiras, sem ficar abaixo do ideal.</summary>
    private decimal Available(StoreStock store, PositionMetrics metrics, decimal effective)
    {
        if (store.Stock < 0)
            return 0;
        var surplus = store.IsWarehouse ? effective : effective - rules.Parameters.IdealDays * metrics.DailyAverage;
        return Math.Max(Math.Floor(surplus), 0);
    }

    private static IEnumerable<StoreStock> OrderOrigins(
        IReadOnlyList<StoreStock> stores, StoreStock destination,
        Dictionary<int, PositionMetrics> metrics, Dictionary<int, decimal> available) =>
        stores
            .Where(s => s.StoreId != destination.StoreId && available[s.StoreId] >= 1)
            .OrderBy(s => s.IsWarehouse ? 0 : 1)
            .ThenByDescending(s => metrics[s.StoreId].CoverageDays ?? decimal.MaxValue)
            .ToList();

    private PlannedTransfer BuildTransfer(
        StoreStock origin, StoreStock destination, decimal quantity,
        Dictionary<int, PositionMetrics> metrics, decimal destinationAfter)
    {
        var o = metrics[origin.StoreId];
        var d = metrics[destination.StoreId];
        var coverageAfter = d.DailyAverage > 0 ? destinationAfter / d.DailyAverage : (decimal?)null;
        return new PlannedTransfer(
            origin.StoreId, destination.StoreId, quantity, d.Priority,
            Explain(origin, destination, quantity, o, d, coverageAfter),
            o, d, coverageAfter, destination.Stock < 0, origin.Stock, destination.Stock);
    }

    private string Explain(StoreStock origin, StoreStock destination, decimal quantity, PositionMetrics o, PositionMetrics d, decimal? coverageAfter)
    {
        var p = rules.Parameters;
        var destinationText = d.Situation == StockSituation.Rupture
            ? $"{destination.StoreLabel} está sem estoque e vende {Number(d.DailyAverage)} un./dia ({Number(destination.Sold12Months, 0)} em 12 meses)."
            : $"{destination.StoreLabel} tem {Number(destination.Stock, 0)} un. para {Number(d.CoverageDays ?? 0, 1)} dias de venda " +
              $"(abaixo do mínimo de {p.MinimumDays}), vendendo {Number(d.DailyAverage)} un./dia.";
        var originText = origin.IsWarehouse
            ? $"{origin.StoreLabel} não vende e tem {Number(origin.Stock, 0)} un."
            : $"{origin.StoreLabel} tem {Number(origin.Stock, 0)} un. " +
              (o.CoverageDays is null ? "e não vendeu nos últimos 12 meses" : $"para {Number(o.CoverageDays.Value, 0)} dias de venda") +
              $"; mesmo cedendo, fica com pelo menos {p.IdealDays} dias.";
        var result = coverageAfter is null ? string.Empty : $" Com {Number(quantity, 0)} un., o destino passa a ter cerca de {Number(coverageAfter.Value, 0)} dias.";
        return destinationText + " " + originText + result;
    }

    private static string Number(decimal value, int decimals = 2) => value.ToString("N" + decimals, PtBr);
}
