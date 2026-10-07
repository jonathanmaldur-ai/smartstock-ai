namespace SmartStock.Api.Security;

public static class Policies
{
    public const string AdminOnly = nameof(AdminOnly);

    /// <summary>Importação de arquivos: Administrador e Operador (PRD).</summary>
    public const string CanImport = nameof(CanImport);

    /// <summary>Gerar a análise do estoque: Administrador, Gerente e Operador.</summary>
    public const string CanAnalyze = nameof(CanAnalyze);

    /// <summary>Aprovar ou rejeitar sugestões: Administrador e Gerente ("aprovação de ações", PRD).</summary>
    public const string CanApprove = nameof(CanApprove);

    /// <summary>Limite de tentativas nos endpoints públicos de autenticação (por IP).</summary>
    public const string AuthRateLimit = nameof(AuthRateLimit);
}
