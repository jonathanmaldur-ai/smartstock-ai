namespace SmartStock.Domain.Catalog;

/// <summary>
/// Unidade da rede. O código de dois dígitos (ex.: "06") é o identificador usado em todas as planilhas.
/// </summary>
public class Store
{
    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? City { get; private set; }
    public StoreType Type { get; private set; }
    public StoreStatus Status { get; private set; }

    private Store() { }

    public Store(string code, string name, string? city, StoreType type, StoreStatus status)
    {
        Code = NormalizeCode(code) ?? throw new ArgumentException("Código de loja inválido.", nameof(code));
        Rename(name, city);
        Type = type;
        Status = status;
    }

    public bool IsActive => Status == StoreStatus.Active;

    public void Rename(string name, string? city)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome da unidade é obrigatório.", nameof(name));
        Name = name.Trim();
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
    }

    /// <summary>"6", "06" e "06 MOGI MIRIM" viram "06". Retorna null se não houver código numérico.</summary>
    public static string? NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var digits = new string(value.Trim().TakeWhile(char.IsDigit).ToArray());
        return digits.Length is > 0 and <= 3 ? digits.PadLeft(2, '0') : null;
    }
}

public enum StoreType
{
    Store = 1,
    Warehouse = 2,
    Ecommerce = 3
}

public enum StoreStatus
{
    Active = 1,
    Closed = 2
}
