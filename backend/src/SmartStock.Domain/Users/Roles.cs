namespace SmartStock.Domain.Users;

/// <summary>
/// Perfis de acesso definidos no PRD (seção "Login e Controle de Usuários").
/// </summary>
public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Gerente = "Gerente";
    public const string Operador = "Operador";
    public const string Consulta = "Consulta";

    public static readonly IReadOnlyList<string> All = [Administrador, Gerente, Operador, Consulta];

    public static bool IsValid(string role) => All.Contains(role);
}
