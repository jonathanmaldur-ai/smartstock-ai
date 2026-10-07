using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartStock.Api.Common;
using SmartStock.Api.Security;
using SmartStock.Application.Users;

namespace SmartStock.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class UsersController(IUserAdminService users) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<UserDto>> List(CancellationToken cancellationToken) =>
        await users.ListAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<CreatedUser>> Create(UserBody body, CancellationToken cancellationToken)
    {
        var result = await users.CreateAsync(new CreateUserRequest(body.FullName, body.Email ?? string.Empty, body.Role), cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UserBody body, CancellationToken cancellationToken) =>
        this.ToActionResult(await users.UpdateAsync(id, new UpdateUserRequest(body.FullName, body.Role), cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<UserDto>> Deactivate(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await users.SetActiveAsync(id, active: false, cancellationToken));

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<UserDto>> Activate(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await users.SetActiveAsync(id, active: true, cancellationToken));

    [HttpPost("{id:guid}/resend-invite")]
    public async Task<IActionResult> ResendInvite(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await users.ResendInviteAsync(id, cancellationToken));
}

/// <summary>E-mail só é usado na criação; na edição ele não pode ser alterado.</summary>
public sealed record UserBody(
    [Required(ErrorMessage = "Informe o nome."), MaxLength(150)] string FullName,
    [EmailAddress(ErrorMessage = "E-mail inválido.")] string? Email,
    [Required(ErrorMessage = "Informe o perfil.")] string Role);
