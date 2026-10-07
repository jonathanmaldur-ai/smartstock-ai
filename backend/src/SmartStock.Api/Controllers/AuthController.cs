using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartStock.Api.Common;
using SmartStock.Api.Security;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Auth;

namespace SmartStock.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(Policies.AuthRateLimit)]
    public async Task<ActionResult<SessionResponse>> Login(LoginBody body, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(body.Email, body.Password, cancellationToken);
        return result.IsSuccess ? StartSession(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<SessionResponse>> Refresh(CancellationToken cancellationToken)
    {
        var token = RefreshTokenCookie.Read(Request);
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var result = await authService.RefreshAsync(token, cancellationToken);
        if (result.IsSuccess)
            return StartSession(result.Value);

        RefreshTokenCookie.Delete(Response);
        return this.ToProblem(result.Error!);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(RefreshTokenCookie.Read(Request), cancellationToken);
        RefreshTokenCookie.Delete(Response);
        return NoContent();
    }

    /// <summary>Sempre responde 202, exista ou não o e-mail.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(Policies.AuthRateLimit)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordBody body, CancellationToken cancellationToken)
    {
        await authService.RequestPasswordResetAsync(body.Email, cancellationToken);
        return Accepted();
    }

    [HttpPost("define-password")]
    [AllowAnonymous]
    [EnableRateLimiting(Policies.AuthRateLimit)]
    public async Task<IActionResult> DefinePassword(DefinePasswordBody body, CancellationToken cancellationToken)
    {
        var purpose = body.Type == PasswordLinkTypes.Invite ? PasswordTokenPurpose.Invite : PasswordTokenPurpose.Reset;
        var result = await authService.DefinePasswordAsync(
            new DefinePasswordRequest(body.UserId, body.Token, body.NewPassword, purpose), cancellationToken);
        return this.ToActionResult(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<SessionUser>> Me(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Unauthorized();

        return this.ToActionResult(await authService.GetSessionUserAsync(userId, cancellationToken));
    }

    private ActionResult<SessionResponse> StartSession(AuthSession session)
    {
        RefreshTokenCookie.Write(Response, session.RefreshToken, session.RefreshTokenExpiresAt);
        return Ok(new SessionResponse(session.AccessToken, session.AccessTokenExpiresAt, session.User));
    }
}

/// <summary>O refresh token não aparece no corpo da resposta: vai só no cookie HttpOnly.</summary>
public sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, SessionUser User);

public sealed record LoginBody(
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido.")] string Email,
    [Required(ErrorMessage = "Informe a senha.")] string Password);

public sealed record ForgotPasswordBody(
    [Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido.")] string Email);

public sealed record DefinePasswordBody(
    [Required] Guid UserId,
    [Required] string Token,
    [Required(ErrorMessage = "Informe a nova senha.")] string NewPassword,
    [Required, AllowedValues(PasswordLinkTypes.Invite, PasswordLinkTypes.Reset)] string Type);

public static class PasswordLinkTypes
{
    public const string Invite = "convite";
    public const string Reset = "recuperacao";
}
