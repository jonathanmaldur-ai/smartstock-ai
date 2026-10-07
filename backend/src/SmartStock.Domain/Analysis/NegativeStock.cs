namespace SmartStock.Domain.Analysis;

/// <summary>
/// Produto com estoque negativo numa loja, numa análise (decisões 3, 26 e 38).
/// O SmartStock não corrige o estoque: aponta prioridade e causas prováveis para a correção no ERP.
/// </summary>
public class NegativeStock
{
    public Guid AnalysisId { get; set; }
    public int ProductId { get; set; }
    public int StoreId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Sold12Months { get; set; }
    public AlertPriority Priority { get; set; }
    public NegativeCause Causes { get; set; }

    /// <summary>Unidades enviadas para a loja por transferência e ainda sem entrada.</summary>
    public decimal PendingTransferUnits { get; set; }

    /// <summary>Código do outro produto (mesma marca e referência) com estoque positivo na loja.</summary>
    public string? DuplicateProductCode { get; set; }
}

/// <summary>Causas prováveis (hipóteses) da decisão 38. Um item pode ter várias.</summary>
[Flags]
public enum NegativeCause
{
    None = 0,
    TransferNotReceived = 1,
    SaleWithoutEntry = 2,
    ShippedWithoutEntry = 4,
    FractionalUnit = 8,
    PossibleDuplicate = 16
}

/// <summary>O que se sabe do produto na loja para classificar o negativo.</summary>
public sealed record NegativeFacts(
    decimal Quantity,
    decimal Sold12Months,
    bool IsWarehouse,
    string? Unit,
    decimal PendingTransferUnits,
    bool ReceivedByTransfer,
    bool ShippedByTransfer,
    string? DuplicateProductCode);

/// <summary>Prioridade e causas prováveis de um estoque negativo (decisão 38).</summary>
public static class NegativeRules
{
    public const decimal LargeQuantity = 10;
    public const decimal MediumQuantity = 3;

    private static readonly HashSet<string> FractionalUnits = new(StringComparer.OrdinalIgnoreCase) { "MT", "M", "KG", "L", "LT" };

    public static AlertPriority Priority(NegativeFacts facts)
    {
        var size = Math.Abs(facts.Quantity);
        if (!facts.IsWarehouse && facts.Sold12Months > 0) return AlertPriority.Critical;
        if (size >= LargeQuantity) return AlertPriority.High;
        if (size >= MediumQuantity) return AlertPriority.Medium;
        return AlertPriority.Low;
    }

    public static NegativeCause Causes(NegativeFacts facts)
    {
        var causes = NegativeCause.None;
        if (facts.PendingTransferUnits > 0)
            causes |= NegativeCause.TransferNotReceived;
        if (!facts.IsWarehouse && facts.Sold12Months > 0 && !facts.ReceivedByTransfer)
            causes |= NegativeCause.SaleWithoutEntry;
        if (facts.ShippedByTransfer && !facts.ReceivedByTransfer && facts.Sold12Months <= 0)
            causes |= NegativeCause.ShippedWithoutEntry;
        if (facts.Quantity % 1 != 0 || (facts.Unit is not null && FractionalUnits.Contains(facts.Unit.Trim())))
            causes |= NegativeCause.FractionalUnit;
        if (facts.DuplicateProductCode is not null)
            causes |= NegativeCause.PossibleDuplicate;
        return causes;
    }
}
