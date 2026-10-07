using SmartStock.Domain.Auditing;

namespace SmartStock.Application.Abstractions;

/// <summary>
/// Registra ações críticas. Usuário e IP vêm de <see cref="ICurrentUser"/>,
/// a menos que sejam informados na entrada (ex.: login, quando ainda não há usuário autenticado).
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

public sealed record AuditEntry(
    string Action,
    AuditResult Result = AuditResult.Success,
    string? EntityType = null,
    string? EntityId = null,
    object? Details = null,
    Guid? UserId = null,
    string? UserEmail = null);
