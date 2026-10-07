using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartStock.Application.Abstractions;
using SmartStock.Domain.Auditing;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Auditing;

internal sealed class AuditLogger(
    SmartStockDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<AuditLogger> logger) : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        var log = new AuditLog(
            clock.GetUtcNow(),
            entry.Action,
            entry.Result,
            entry.UserId ?? currentUser.UserId,
            entry.UserEmail ?? currentUser.Email,
            entry.EntityType,
            entry.EntityId,
            entry.Details is null ? null : JsonSerializer.Serialize(entry.Details, JsonOptions),
            currentUser.IpAddress);

        db.AuditLogs.Add(log);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Auditoria: {Action} {Result} por {UserEmail} ({EntityType} {EntityId})",
            log.Action, log.Result, log.UserEmail ?? "anônimo", log.EntityType, log.EntityId);
    }
}
