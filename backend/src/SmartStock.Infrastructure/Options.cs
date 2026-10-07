using System.ComponentModel.DataAnnotations;

namespace SmartStock.Infrastructure;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required] public string Issuer { get; set; } = "SmartStock";
    [Required] public string Audience { get; set; } = "SmartStock";

    /// <summary>Segredo de assinatura (mínimo 32 caracteres). Nunca fica em arquivo do projeto.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = string.Empty;

    [Range(1, 60)] public int AccessTokenMinutes { get; set; } = 15;
    [Range(1, 30)] public int RefreshTokenDays { get; set; } = 7;
}

public sealed class AccountOptions
{
    public const string SectionName = "Accounts";

    /// <summary>Domínios de e-mail corporativo aceitos no cadastro de usuários.</summary>
    [Required, MinLength(1)] public string[] AllowedEmailDomains { get; set; } = [];

    /// <summary>Administrador criado automaticamente quando o banco não tem nenhum usuário.</summary>
    [Required, EmailAddress] public string InitialAdminEmail { get; set; } = string.Empty;
    [Required] public string InitialAdminName { get; set; } = "Administrador";

    [Range(1, 168)] public int InviteLinkHours { get; set; } = 72;
    [Range(1, 24)] public int ResetLinkHours { get; set; } = 2;
}

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Endereço público da interface, usado nos links enviados por e-mail.</summary>
    [Required, Url] public string PublicUrl { get; set; } = string.Empty;
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Smtp" envia de verdade. "File" grava o e-mail em disco (somente desenvolvimento).</summary>
    public EmailDeliveryMode Mode { get; set; } = EmailDeliveryMode.File;

    public string PickupDirectory { get; set; } = "emails-dev";

    public string? Host { get; set; }
    public int Port { get; set; } = 465;

    /// <summary>SslOnConnect (porta 465), StartTls (porta 587) ou None.</summary>
    public string Security { get; set; } = "SslOnConnect";

    public string? Username { get; set; }
    public string? Password { get; set; }

    [Required, EmailAddress] public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "SmartStock AI";
}

public enum EmailDeliveryMode
{
    File,
    Smtp
}
