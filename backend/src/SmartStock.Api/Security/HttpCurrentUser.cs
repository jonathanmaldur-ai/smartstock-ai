using Microsoft.IdentityModel.JsonWebTokens;
using SmartStock.Application.Abstractions;

namespace SmartStock.Api.Security;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext? Context => accessor.HttpContext;

    public Guid? UserId =>
        Guid.TryParse(Context?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    public string? Email => Context?.User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();
}
