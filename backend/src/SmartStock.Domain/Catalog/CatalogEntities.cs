namespace SmartStock.Domain.Catalog;

public class Brand
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? LastImportId { get; set; }
}

/// <summary>Categoria oficial (coluna "Categoria" da planilha, depois da correção de-para).</summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Itens que não são mercadoria (vale-presente, frete...) ou bebidas: ficam fora de todas as análises.</summary>
    public bool ExcludedFromAnalysis { get; set; }

    public List<CategoryAlias> Aliases { get; set; } = [];
}

/// <summary>Grafia encontrada nas planilhas que corresponde a uma categoria oficial (ex.: "BRIQUEDOS" → BRINQUEDOS).</summary>
public class CategoryAlias
{
    public int Id { get; set; }

    /// <summary>Chave normalizada (<see cref="CatalogText.Key"/>).</summary>
    public string Alias { get; set; } = string.Empty;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

/// <summary>Coluna "Categoria 2" da planilha.</summary>
public class Subcategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Product
{
    public int Id { get; set; }

    /// <summary>Código do produto como texto (preserva zeros à esquerda, ex.: "0000000001513").</summary>
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? Reference { get; set; }
    public string? FiscalDescription { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Fora de época (ex.: figurinhas da Copa): não gera sugestão de transferência nem de compra (decisão 37).</summary>
    public bool IsSeasonal { get; set; }

    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int? SubcategoryId { get; set; }
    public Subcategory? Subcategory { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? LastImportId { get; set; }
}
