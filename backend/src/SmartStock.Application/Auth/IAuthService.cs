using SmartStock.Application.Common;

namespace SmartStock.Application.Auth;

public interface IAuthService
{
    Task<Result<AuthSession>> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Troca um refresh token válido por uma nova sessão (o token antigo é revogado).</summary>
    Task<Result<AuthSession>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Sempre conclui sem erro, para não revelar se o e-mail existe.</summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Define a senha a partir de um link de convite (primeiro acesso) ou de recuperação.</summary>
    Task<Result> DefinePasswordAsync(DefinePasswordRequest request, CancellationToken cancellationToken = default);

    Task<Result<SessionUser>> GetSessionUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record AuthSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    SessionUser User);

public sealed record SessionUser(Guid Id, string Email, string FullName, string Role);

public enum PasswordTokenPurpose
{
    Invite,
    Reset
}

public sealed record DefinePasswordRequest(Guid UserId, string Token, string NewPassword, PasswordTokenPurpose Purpose);
