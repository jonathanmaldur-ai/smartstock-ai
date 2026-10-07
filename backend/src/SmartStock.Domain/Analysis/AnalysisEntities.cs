namespace SmartStock.Domain.Analysis;

/// <summary>
/// Uma análise do estoque: foto de estoque + vendas atuais + parâmetros, calculada quando o usuário pede.
/// Guarda os parâmetros usados, para cada sugestão poder ser explicada depois.
/// </summary>
public class StockAnalysis
{
    public Guid Id { get; set; }
    public Guid StockImportId { get; set; }

    /// <summary>Data da foto de estoque usada.</summary>
    public DateOnly StockDate { get; set; }

    /// <summary>Dia da análise: o estoque é projetado até aqui (decisão 18).</summary>
    public DateOnly AnalysisDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedByEmail { get; set; } = string.Empty;

    /// <summary>Parâmetros usados (JSON).</summary>
    public string Parameters { get; set; } = "{}";

    public int PositionCount { get; set; }
    public int SuggestionCount { get; set; }
    public decimal SuggestedUnits { get; set; }
    public int PurchaseCount { get; set; }
}

/// <summary>Situação de um produto numa loja, numa análise.</summary>
public class StockPosition
{
    public Guid AnalysisId { get; set; }
    public int ProductId { get; set; }
    public int StoreId { get; set; }

    /// <summary>Estoque importado, como veio (pode ser negativo).</summary>
    public decimal Stock { get; set; }

    /// <summary>Estoque importado − VMD × dias desde a foto (decisão 18).</summary>
    public decimal ProjectedStock { get; set; }

    public decimal Sold12Months { get; set; }
    public decimal DailyAverage { get; set; }
    public decimal? CoverageDays { get; set; }
    public StockSituation Situation { get; set; }
    public AlertPriority Priority { get; set; }
}

/// <summary>
/// Recomendação de transferência (a IA e o sistema nunca executam: CLAUDE.md).
/// Guarda os dados usados dos dois lados, para a explicação ficar rastreável.
/// </summary>
public class TransferSuggestion
{
    public long Id { get; set; }
    public Guid AnalysisId { get; set; }
    public int ProductId { get; set; }
    public int OriginStoreId { get; set; }
    public int DestinationStoreId { get; set; }
    public decimal Quantity { get; set; }
    public AlertPriority Priority { get; set; }
    public SuggestionStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;

    public decimal OriginStock { get; set; }
    public decimal OriginDailyAverage { get; set; }
    public decimal? OriginCoverageDays { get; set; }
    public decimal DestinationStock { get; set; }
    public decimal DestinationDailyAverage { get; set; }
    public decimal? DestinationCoverageDays { get; set; }
    public decimal? DestinationCoverageAfter { get; set; }

    /// <summary>Destino com estoque negativo: "confirme o estoque físico" (decisão 26).</summary>
    public bool DestinationNegative { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedByEmail { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>Data da saída encontrada no arquivo de transferências (decisão 9).</summary>
    public DateOnly? CompletedOn { get; set; }

    /// <summary>Quantidade que saiu na rota a partir da aprovação (pode diferir da sugerida).</summary>
    public decimal? CompletedQuantity { get; set; }
}

/// <summary>Falta que nenhuma loja da rede consegue cobrir: recomendação de compra (decisão 10), nunca pedido.</summary>
public class PurchaseSuggestion
{
    public long Id { get; set; }
    public Guid AnalysisId { get; set; }
    public int ProductId { get; set; }
    public int StoreId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Stock { get; set; }
    public decimal DailyAverage { get; set; }
    public decimal? CoverageDays { get; set; }
}

public enum StockSituation
{
    /// <summary>Vende e está sem estoque.</summary>
    Rupture = 1,

    /// <summary>Cobertura abaixo do crítico (7 dias).</summary>
    CriticalCoverage = 2,

    /// <summary>Cobertura abaixo do mínimo (15 dias).</summary>
    BelowMinimum = 3,

    Normal = 4,

    /// <summary>Cobertura acima do excesso (120 dias).</summary>
    Excess = 5,

    /// <summary>Estoque positivo e nenhuma venda em 12 meses.</summary>
    Stagnant = 6,

    /// <summary>Sem venda e sem estoque positivo (ex.: só estoque negativo).</summary>
    NoMovement = 7,

    /// <summary>Depósito: não vende, só abastece.</summary>
    Warehouse = 8
}

public enum AlertPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum SuggestionStatus
{
    Suggested = 1,
    Approved = 2,
    Rejected = 3,

    /// <summary>Apareceu no arquivo de transferências importado (decisão 9).</summary>
    Completed = 4,

    /// <summary>Uma análise mais nova recalculou as sugestões pendentes.</summary>
    Superseded = 5
}
