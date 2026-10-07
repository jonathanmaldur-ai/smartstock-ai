using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Auth;
using SmartStock.Infrastructure.Identity;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Auth;

/// <summary>
/// Emite o access token (JWT curto, fica só na memória do navegador) e o refresh token
/// (longo, em cookie HttpOnly), além de rotacionar e revogar refresh tokens.
/// </summary>
internal sealed class SessionTokenService(
    SmartStockDbContext db,
    IOptions<JwtOptions> jwtOptions,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthSession> IssueAsync(AppUser user, string role, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var refreshToken = GenerateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Hash(refreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
            CreatedByIp = currentUser.IpAddress
        });
        await db.SaveChangesAsync(cancellationToken);

        var sessionUser = new SessionUser(user.Id, user.Email!, user.FullName, role);
        var accessExpiresAt = now.AddMinutes(_jwt.AccessTokenMinutes);

        return new AuthSession(
            CreateAccessToken(sessionUser, now, accessExpiresAt),
            accessExpiresAt,
            refreshToken,
            now.AddDays(_jwt.RefreshTokenDays),
            sessionUser);
    }

    public Task<RefreshToken?> FindAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = Hash(refreshToken);
        return db.RefreshTokens.Include(t => t.User).SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
    }

    /// <summary>Revoga o token usado e emite uma nova sessão (rotação).</summary>
    public async Task<AuthSession> RotateAsync(RefreshToken current, string role, CancellationToken cancellationToken)
    {
        var session = await IssueAsync(current.User, role, cancellationToken);
        current.RevokedAt = clock.GetUtcNow();
        current.ReplacedByTokenHash = Hash(session.RefreshToken);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task RevokeAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        token.RevokedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
    }

    private string CreateAccessToken(SessionUser user, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.FullName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("role", user.Role)
            ])
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string GenerateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
