using SmartStock.Application.Common;
using SmartStock.Domain.Auditing;

namespace SmartStock.Application.Auditing;

public interface IAuditQueryService
{
    Task<PagedResult<AuditLogDto>> SearchAsync(AuditQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken = default);
}

public sealed record AuditQuery(
    int Page = 1,
    int PageSize = 25,
    string? Action = null,
    string? UserEmail = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    AuditResult? Result = null)
{
    public const int MaxPageSize = 100;
}

public sealed record AuditLogDto(
    long Id,
    DateTimeOffset OccurredAt,
    string? UserEmail,
    string Action,
    string? EntityType,
    string? EntityId,
    AuditResult Result,
    string? Details,
    string? IpAddress);
