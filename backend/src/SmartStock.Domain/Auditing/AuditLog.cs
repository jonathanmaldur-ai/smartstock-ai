namespace SmartStock.Domain.Auditing;

/// <summary>
/// Registro imutável de uma ação crítica (CLAUDE.md, seção "Auditoria").
/// </summary>
public class AuditLog
{
    public long Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? UserId { get; private set; }
    public string? UserEmail { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public AuditResult Result { get; private set; }

    /// <summary>JSON com os dados afetados.</summary>
    public string? Details { get; private set; }

    public string? IpAddress { get; private set; }

    private AuditLog() { }

    public AuditLog(
        DateTimeOffset occurredAt,
        string action,
        AuditResult result,
        Guid? userId,
        string? userEmail,
        string? entityType,
        string? entityId,
        string? details,
        string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("A ação é obrigatória.", nameof(action));

        OccurredAt = occurredAt;
        Action = action;
        Result = result;
        UserId = userId;
        UserEmail = userEmail;
        EntityType = entityType;
        EntityId = entityId;
        Details = details;
        IpAddress = ipAddress;
    }
}

public enum AuditResult
{
    Success = 1,
    Failure = 2
}
