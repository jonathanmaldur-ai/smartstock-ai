using SmartStock.Application.Common;

namespace SmartStock.Application.Users;

/// <summary>
/// Gestão de usuários, restrita ao perfil Administrador.
/// Usuários nunca são excluídos: são desativados (soft delete), para manter a rastreabilidade.
/// </summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<CreatedUser>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<Result<UserDto>> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task<Result<UserDto>> SetActiveAsync(Guid userId, bool active, CancellationToken cancellationToken = default);
    Task<Result> ResendInviteAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record UserDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    bool IsActive,
    bool HasPassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// <summary>Usuário criado e se o e-mail de convite foi enviado (se falhar, o Administrador pode reenviar).</summary>
public sealed record CreatedUser(UserDto User, bool InviteSent);

public sealed record CreateUserRequest(string FullName, string Email, string Role);

public sealed record UpdateUserRequest(string FullName, string Role);
