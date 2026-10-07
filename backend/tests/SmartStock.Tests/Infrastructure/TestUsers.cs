using System.Net.Http.Json;

namespace SmartStock.Tests.Infrastructure;

public static class TestUsers
{
    public const string DefaultPassword = "SenhaDoTeste1";

    /// <summary>Cria um usuário (convite pendente) pelo Administrador, com e-mail único.</summary>
    public static async Task<(string Email, Guid Id)> CreateUserAsync(this SmartStockApiFactory factory, string role)
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = $"teste.{Guid.NewGuid():N}@example.com";

        var response = await admin.PostAsJsonAsync("/api/users", new { fullName = "Usuário Teste", email, role });
        response.EnsureSuccessStatusCode();
        var created = await response.ReadAsync<CreatedUserDto>();
        return (email, created.User.Id);
    }

    /// <summary>Cria um usuário e já define a senha pelo link de convite.</summary>
    public static async Task<(string Email, string Password)> CreateActiveUserAsync(this SmartStockApiFactory factory, string role)
    {
        var (email, _) = await factory.CreateUserAsync(role);
        using var client = factory.CreateHttpsClient();
        (await client.DefinePasswordAsync(factory.Emails.LastLinkFor(email), DefaultPassword)).EnsureSuccessStatusCode();
        return (email, DefaultPassword);
    }

    public static async Task<HttpClient> CreateAdminClientAsync(this SmartStockApiFactory factory)
    {
        var client = factory.CreateHttpsClient();
        await client.SignInAsync(SmartStockApiFactory.AdminEmail, SmartStockApiFactory.AdminPassword);
        return client;
    }

    public static async Task<HttpClient> CreateClientForRoleAsync(this SmartStockApiFactory factory, string role)
    {
        var (email, password) = await factory.CreateActiveUserAsync(role);
        var client = factory.CreateHttpsClient();
        await client.SignInAsync(email, password);
        return client;
    }
}

public sealed record UserDto(
    Guid Id, string FullName, string Email, string Role, bool IsActive, bool HasPassword,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record CreatedUserDto(UserDto User, bool InviteSent);
