namespace SmartStock.Application.Abstractions;

/// <summary>
/// Dados de quem está fazendo a requisição atual. Nulo quando anônimo.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    string? IpAddress { get; }
}
