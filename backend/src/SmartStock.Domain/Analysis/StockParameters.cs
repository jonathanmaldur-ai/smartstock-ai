namespace SmartStock.Domain.Analysis;

/// <summary>
/// Parâmetros de estoque da seção 2 das decisões (decisão 8) e venda mínima da decisão 35.
/// Registro único, editável pelo Administrador.
/// </summary>
public class StockParameters
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Abaixo disso (em dias de VMD) a loja é destino de transferência.</summary>
    public int MinimumDays { get; set; } = 15;

    /// <summary>A transferência leva o destino até aqui; a origem nunca fica abaixo disso.</summary>
    public int IdealDays { get; set; } = 30;

    public int MaximumDays { get; set; } = 60;

    /// <summary>Cobertura acima disso é excesso.</summary>
    public int ExcessDays { get; set; } = 120;

    /// <summary>Cobertura abaixo disso é alerta alto.</summary>
    public int CriticalCoverageDays { get; set; } = 7;

    /// <summary>Venda mínima em 12 meses no destino para ruptura crítica e sugestão (decisão 35).</summary>
    public decimal MinimumAnnualSales { get; set; } = 12;

    /// <summary>Após cada análise, envia o resumo dos alertas aos Administradores e Gerentes (decisão 47).</summary>
    public bool SendAlertEmail { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedByEmail { get; set; }

    /// <summary>Mensagem do problema, ou null se os valores são coerentes.</summary>
    public string? Validate()
    {
        if (CriticalCoverageDays < 1 || MinimumDays < 1 || IdealDays < 1 || MaximumDays < 1 || ExcessDays < 1)
            return "Os dias devem ser maiores que zero.";
        if (!(CriticalCoverageDays <= MinimumDays && MinimumDays < IdealDays && IdealDays <= MaximumDays && MaximumDays < ExcessDays))
            return "Os dias devem seguir a ordem: crítico ≤ mínimo < ideal ≤ máximo < excesso.";
        if (MinimumAnnualSales < 0)
            return "A venda mínima não pode ser negativa.";
        return null;
    }
}
