namespace SmartStock.Api.Security;

/// <summary>
/// O refresh token vive só neste cookie: HttpOnly (JavaScript não lê), Secure e SameSite=Strict,
/// restrito às rotas de autenticação.
/// </summary>
internal static class RefreshTokenCookie
{
    public const string Name = "smartstock_refresh";
    private const string Path = "/api/auth";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, token, BuildOptions(expiresAt));

    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, BuildOptions(null));

    private static CookieOptions BuildOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAt,
        IsEssential = true
    };
}
