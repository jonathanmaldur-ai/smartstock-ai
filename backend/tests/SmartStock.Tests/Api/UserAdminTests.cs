using System.Net;
using System.Net.Http.Json;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;

namespace SmartStock.Tests.Api;

[Collection(ApiCollection.Name)]
public sealed class UserAdminTests(SmartStockApiFactory factory)
{
    [Fact]
    public async Task Administrador_cria_usuario_e_convite_e_enviado()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = $"novo.{Guid.NewGuid():N}@example.com";

        var response = await admin.PostAsJsonAsync("/api/users", new { fullName = "Gerente Taubaté", email, role = Roles.Gerente });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<CreatedUserDto>();
        Assert.True(created.InviteSent);
        Assert.Equal(Roles.Gerente, created.User.Role);
        Assert.False(created.User.HasPassword);
        Assert.Equal(1, factory.Emails.CountFor(email));
    }

    [Theory]
    [InlineData("alguem@gmail.com", "user.email_domain")]
    [InlineData("sem-arroba", null)]
    public async Task Email_fora_do_dominio_corporativo_e_recusado(string email, string? expectedCode)
    {
        using var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/users", new { fullName = "Fulano", email, role = Roles.Consulta });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (expectedCode is not null)
            Assert.Equal(expectedCode, await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Email_duplicado_e_recusado()
    {
        var (email, _) = await factory.CreateUserAsync(Roles.Consulta);
        using var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/users", new { fullName = "Outro", email = email.ToUpperInvariant(), role = Roles.Consulta });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Gerente)]
    [InlineData(Roles.Operador)]
    [InlineData(Roles.Consulta)]
    public async Task Somente_Administrador_gerencia_usuarios(string role)
    {
        using var client = await factory.CreateClientForRoleAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit")).StatusCode);
    }

    [Fact]
    public async Task Administrador_nao_pode_desativar_a_si_mesmo()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var me = await admin.GetFromJsonAsync<SessionUserDto>("/api/auth/me", ApiClientExtensions.Json);

        var response = await admin.PostAsync($"/api/users/{me!.Id}/deactivate", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_desativado_perde_o_acesso_e_pode_ser_reativado()
    {
        var (email, password) = await factory.CreateActiveUserAsync(Roles.Operador);
        using var admin = await factory.CreateAdminClientAsync();
        var user = (await admin.GetFromJsonAsync<List<UserDto>>("/api/users", ApiClientExtensions.Json))!.Single(u => u.Email == email);
        using var client = factory.CreateHttpsClient();

        (await admin.PostAsync($"/api/users/{user.Id}/deactivate", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(email, password)).StatusCode);

        (await admin.PostAsync($"/api/users/{user.Id}/activate", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, password)).StatusCode);
    }

    [Fact]
    public async Task Alterar_perfil_vale_no_proximo_login()
    {
        var (email, password) = await factory.CreateActiveUserAsync(Roles.Consulta);
        using var admin = await factory.CreateAdminClientAsync();
        var user = (await admin.GetFromJsonAsync<List<UserDto>>("/api/users", ApiClientExtensions.Json))!.Single(u => u.Email == email);

        var update = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { fullName = "Nome Alterado", role = Roles.Gerente });
        update.EnsureSuccessStatusCode();

        using var client = factory.CreateHttpsClient();
        var session = await client.SignInAsync(email, password);
        Assert.Equal(Roles.Gerente, session.User.Role);
        Assert.Equal("Nome Alterado", session.User.FullName);
    }

    [Fact]
    public async Task Reenvio_de_convite_so_para_quem_ainda_nao_ativou()
    {
        var (pendingEmail, pendingId) = await factory.CreateUserAsync(Roles.Consulta);
        var (activeEmail, _) = await factory.CreateActiveUserAsync(Roles.Consulta);
        using var admin = await factory.CreateAdminClientAsync();
        var active = (await admin.GetFromJsonAsync<List<UserDto>>("/api/users", ApiClientExtensions.Json))!.Single(u => u.Email == activeEmail);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{pendingId}/resend-invite", null)).StatusCode);
        Assert.Equal(2, factory.Emails.CountFor(pendingEmail));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/users/{active.Id}/resend-invite", null)).StatusCode);
    }
}
