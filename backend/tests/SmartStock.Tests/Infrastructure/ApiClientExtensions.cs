using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartStock.Tests.Infrastructure;

public static class ApiClientExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    public static Task<HttpResponseMessage> DefinePasswordAsync(this HttpClient client, PasswordLink link, string newPassword) =>
        client.PostAsJsonAsync("/api/auth/define-password", new
        {
            userId = link.UserId,
            token = link.Token,
            newPassword,
            type = link.Type
        });

    /// <summary>Faz login e passa a enviar o access token em todas as requisições deste cliente.</summary>
    public static async Task<SessionDto> SignInAsync(this HttpClient client, string email, string password)
    {
        var response = await client.LoginAsync(email, password);
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<SessionDto>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return problem.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}

public sealed record SessionDto(string AccessToken, DateTimeOffset ExpiresAt, SessionUserDto User);

public sealed record SessionUserDto(Guid Id, string Email, string FullName, string Role);
