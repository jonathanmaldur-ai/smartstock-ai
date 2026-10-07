using Microsoft.AspNetCore.Identity;

namespace SmartStock.Infrastructure.Identity;

/// <summary>
/// Usuário da plataforma. O login é o e-mail corporativo (UserName = Email).
/// Nunca é excluído: é desativado, para manter a rastreabilidade da auditoria.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
}
