using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Auditing;
using SmartStock.Application.Common;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Auditing;

internal sealed class AuditQueryService(SmartStockDbContext db) : IAuditQueryService
{
    public async Task<PagedResult<AuditLogDto>> SearchAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, AuditQuery.MaxPageSize);

        var logs = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Action))
            logs = logs.Where(a => a.Action == query.Action);

        if (!string.IsNullOrWhiteSpace(query.UserEmail))
            logs = logs.Where(a => a.UserEmail != null && EF.Functions.ILike(a.UserEmail, $"%{query.UserEmail.Trim()}%"));

        if (query.From is not null)
            logs = logs.Where(a => a.OccurredAt >= query.From);

        if (query.To is not null)
            logs = logs.Where(a => a.OccurredAt <= query.To);

        if (query.Result is not null)
            logs = logs.Where(a => a.Result == query.Result);

        var total = await logs.CountAsync(cancellationToken);

        var items = await logs
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(
                a.Id, a.OccurredAt, a.UserEmail, a.Action, a.EntityType, a.EntityId, a.Result, a.Details, a.IpAddress))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken = default) =>
        await db.AuditLogs.AsNoTracking()
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(cancellationToken);
}
