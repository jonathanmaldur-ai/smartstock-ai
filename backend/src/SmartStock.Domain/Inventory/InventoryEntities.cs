namespace SmartStock.Domain.Inventory;

/// <summary>
/// Quantidade de um produto numa loja, na foto do estoque de uma importação (decisão 31).
/// Só quantidades diferentes de zero são gravadas: produto sem linha na foto tem estoque zero naquela loja.
/// </summary>
public class StockLevel
{
    public Guid ImportId { get; set; }
    public int ProductId { get; set; }
    public int StoreId { get; set; }

    /// <summary>Como veio do ERP: pode ser negativa (inconsistência) ou fracionada.</summary>
    public decimal Quantity { get; set; }
}

/// <summary>Venda acumulada de um produto numa loja, no período de 12 meses da importação (decisões 1 e 32).</summary>
public class SalesTotal
{
    public Guid ImportId { get; set; }
    public int ProductId { get; set; }
    public int StoreId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? Amount { get; set; }
}

/// <summary>
/// Uma linha do relatório de transferências do ERP (decisão 33): a saída é registrada na origem quando a mercadoria
/// é enviada; a entrada, no destino quando é recebida. No cancelamento, a entrada devolve a mercadoria à origem.
/// </summary>
public class TransferMovement
{
    public long Id { get; set; }
    public Guid ImportId { get; set; }
    public DateOnly Date { get; set; }
    public int ProductId { get; set; }
    public int OriginStoreId { get; set; }
    public int DestinationStoreId { get; set; }
    public TransferDirection Direction { get; set; }
    public bool IsCancellation { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Usuário do ERP que registrou a movimentação.</summary>
    public string? UserName { get; set; }

    /// <summary>Linha do arquivo original (rastreabilidade).</summary>
    public int SourceRow { get; set; }
}

public enum TransferDirection
{
    /// <summary>Saída (coluna SAIDA do relatório).</summary>
    Out = 1,

    /// <summary>Entrada (coluna ENTRADA do relatório).</summary>
    In = 2
}

/// <summary>
/// Venda de uma loja num dia (decisão 51): soma das vendas do relatório diário do ERP. Só totais: o arquivo traz
/// nome e código de cliente, que não são guardados (LGPD).
/// </summary>
public class DailySale
{
    public int StoreId { get; set; }
    public DateOnly Date { get; set; }
    public Guid ImportId { get; set; }

    /// <summary>Quantidade de vendas (cupons) no dia.</summary>
    public int Sales { get; set; }

    public decimal Pieces { get; set; }

    /// <summary>Valor antes de descontos e ajustes.</summary>
    public decimal Gross { get; set; }

    /// <summary>Valor final das vendas.</summary>
    public decimal Net { get; set; }
}

/// <summary>Produto vendido numa loja num dia (decisão 52): soma dos itens do relatório de vendas detalhado do ERP.</summary>
public class DailySaleItem
{
    public int StoreId { get; set; }
    public DateOnly Date { get; set; }
    public int ProductId { get; set; }
    public Guid ImportId { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Em quantas vendas (cupons) o produto saiu no dia (decisão 53).</summary>
    public int Sales { get; set; }

    /// <summary>Valor dos itens (quantidade × valor unitário, como no relatório).</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// Uma venda (cupom ou nota) de uma loja, do relatório de vendas detalhado do ERP (decisão 53). Sem nome de cliente nem
/// vendedor (LGPD): só se o cliente era o de balcão ou um cliente cadastrado.
/// </summary>
public class StoreSale
{
    public int StoreId { get; set; }

    /// <summary>Número da venda no ERP ("vennum").</summary>
    public string Number { get; set; } = string.Empty;

    public DateOnly Date { get; set; }
    public Guid ImportId { get; set; }
    public decimal Gross { get; set; }
    public decimal Net { get; set; }

    /// <summary>false = "CLIENTE BALCAO"; true = cliente identificado (o nome não é guardado).</summary>
    public bool RegisteredCustomer { get; set; }

    /// <summary>Forma de pagamento como no ERP ("01-A VISTA", "CREDITO 2X").</summary>
    public string? Payment { get; set; }

    /// <summary>Documento fiscal ("NF-e nº 007420", "NFC-e nº 000027354", "Bloco de Notas nº 000132542").</summary>
    public string? FiscalDocument { get; set; }
}

/// <summary>Um produto de uma venda (decisão 53).</summary>
public class StoreSaleLine
{
    public long Id { get; set; }
    public int StoreId { get; set; }
    public string SaleNumber { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}
