using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStock.Api.Security;
using SmartStock.Application.Auditing;
using SmartStock.Application.Common;
using SmartStock.Domain.Auditing;

namespace SmartStock.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class AuditController(IAuditQueryService auditQuery) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditLogDto>> Search(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? action = null,
        [FromQuery] string? userEmail = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] AuditResult? result = null,
        CancellationToken cancellationToken = default) =>
        auditQuery.SearchAsync(new AuditQuery(page, pageSize, action, userEmail, from, to, result), cancellationToken);

    [HttpGet("actions")]
    public Task<IReadOnlyList<string>> Actions(CancellationToken cancellationToken) =>
        auditQuery.ListActionsAsync(cancellationToken);
}
