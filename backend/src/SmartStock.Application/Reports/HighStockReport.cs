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
        new("Venda 30 dias", ReportColumnKind.Decimal), new("Cobertura (dias)", ReportColumnKind.Decimal)
    ];

    public const int GroupColumn = 0;
    public const int CategoryColumn = 5;
    public const int TotalColumn = 7;

    /// <summary>Casa e Decor é a categoria HOME do ERP; Brinquedos, a categoria BRINQUEDOS (já com as grafias corrigidas).</summary>
    public static string GroupOf(string category) => category switch
    {
        "HOME" => HomeGroup,
        "BRINQUEDOS" => ToysGroup,
        _ => OtherGroup
    };
}
