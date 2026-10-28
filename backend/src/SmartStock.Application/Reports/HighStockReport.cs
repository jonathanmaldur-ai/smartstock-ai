namespace SmartStock.Application.Reports;

/// <summary>
/// Produtos com estoque alto (decisão 64): produtos com mais unidades que o mínimo, somando lojas e depósito,
/// com a quantidade de cada loja. Pensado para campanhas e queima de estoque (ex.: Black Friday).
/// </summary>
public static class HighStockReport
{
    public const string Key = "estoque-alto";
    public const decimal DefaultMinQuantity = 300;

    public const string HomeGroup = "Casa e Decor";
    public const string ToysGroup = "Brinquedos";
    public const string OtherGroup = "Outras categorias";
    public static readonly IReadOnlyList<string> Groups = [HomeGroup, ToysGroup, OtherGroup];

    /// <summary>Colunas fixas antes das colunas de loja (uma por loja ativa, na ordem do código).</summary>
    public static readonly IReadOnlyList<ReportColumn> FixedColumns =
    [
        new("Grupo", ReportColumnKind.Text), new("Código", ReportColumnKind.Text), new("Produto", ReportColumnKind.Text),
        new("Referência", ReportColumnKind.Text), new("Marca", ReportColumnKind.Text), new("Categoria", ReportColumnKind.Text),
        new("Subcategoria", ReportColumnKind.Text), new("Total (un.)", ReportColumnKind.Decimal),
        new("Negativos na rede", ReportColumnKind.Decimal), new("Total líquido", ReportColumnKind.Decimal),
        new("Venda 30 dias", ReportColumnKind.Decimal), new("Cobertura (dias)", ReportColumnKind.Decimal),
        new("Conferir estoque", ReportColumnKind.Text), new("Época", ReportColumnKind.Text)
    ];

    public const int GroupColumn = 0;
    public const int CategoryColumn = 5;
    public const int TotalColumn = 7;
    public const int CheckColumn = 12;

    /// <summary>Negativos acima desta fração do total: provável erro de lançamento no ERP (ex.: transferência espelhada).</summary>
    public const decimal NegativeShareToCheck = 0.10m;

    /// <summary>Cobertura acima de 5 anos: quantidade fora da realidade da venda (ex.: digitação errada).</summary>
    public const decimal CoverageDaysToCheck = 5 * 365;

    /// <summary>
    /// Motivo para conferir o estoque no ERP, ou null. O total do relatório é o estoque físico (só positivos); os
    /// negativos e a cobertura apontam quando esse número pode estar errado.
    /// </summary>
    public static string? CheckReason(decimal total, decimal negatives, decimal? coverageDays)
    {
        var reasons = new List<string>();
        if (-negatives > total * NegativeShareToCheck)
            reasons.Add("Negativos altos");
        if (coverageDays > CoverageDaysToCheck)
            reasons.Add("Cobertura acima de 5 anos");
        return reasons.Count == 0 ? null : string.Join(" e ", reasons);
    }

    /// <summary>Casa e Decor é a categoria HOME do ERP; Brinquedos, a categoria BRINQUEDOS (já com as grafias corrigidas).</summary>
    public static string GroupOf(string category) => category switch
    {
        "HOME" => HomeGroup,
        "BRINQUEDOS" => ToysGroup,
        _ => OtherGroup
    };
}
