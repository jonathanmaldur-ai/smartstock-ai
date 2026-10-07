using System.Net;
using System.Net.Http.Json;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;

namespace SmartStock.Tests.Api;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(SmartStockApiFactory factory)
{
    [Fact]
    public async Task Admin_inicial_recebe_convite_e_entra_com_a_senha_criada()
    {
        using var client = factory.CreateHttpsClient();

        var session = await client.SignInAsync(SmartStockApiFactory.AdminEmail, SmartStockApiFactory.AdminPassword);

        Assert.Equal(Roles.Administrador, session.User.Role);
        var me = await client.GetFromJsonAsync<SessionUserDto>("/api/auth/me", ApiClientExtensions.Json);
        Assert.Equal(SmartStockApiFactory.AdminEmail, me!.Email);
    }

    [Fact]
    public async Task Link_de_convite_nao_pode_ser_usado_duas_vezes()
    {
        var (email, _) = await CreateUserAsync(Roles.Consulta);
        var link = factory.Emails.LastLinkFor(email);
        using var client = factory.CreateHttpsClient();

        (await client.DefinePasswordAsync(link, "PrimeiraSenha1")).EnsureSuccessStatusCode();
        var reuse = await client.DefinePasswordAsync(link, "SegundaSenha1");

        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal("auth.invalid_link", await reuse.ProblemCodeAsync());
    }

    [Fact]
    public async Task Senha_fraca_e_recusada_com_mensagem_em_portugues()
    {
        var (email, _) = await CreateUserAsync(Roles.Consulta);
        using var client = factory.CreateHttpsClient();

        var response = await client.DefinePasswordAsync(factory.Emails.LastLinkFor(email), "curta");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("pelo menos 10 caracteres", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Usuario_sem_senha_definida_nao_consegue_entrar()
    {
        var (email, _) = await CreateUserAsync(Roles.Consulta);
        using var client = factory.CreateHttpsClient();

        var response = await client.LoginAsync(email, "QualquerSenha1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cinco_senhas_erradas_bloqueiam_o_acesso()
    {
        var (email, password) = await CreateActiveUserAsync(Roles.Consulta);
        using var client = factory.CreateHttpsClient();

        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(email, "SenhaErrada99")).StatusCode);

        var fifth = await client.LoginAsync(email, "SenhaErrada99");
        var correctAfterLock = await client.LoginAsync(email, password);

        Assert.Equal(HttpStatusCode.Locked, fifth.StatusCode);
        Assert.Equal(HttpStatusCode.Locked, correctAfterLock.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotaciona_o_token_e_reuso_do_antigo_encerra_todas_as_sessoes()
    {
        var (email, password) = await CreateActiveUserAsync(Roles.Consulta);
        using var client = factory.CreateHttpsClient(handleCookies: false);

        var original = RefreshCookieFrom(await client.LoginAsync(email, password));
        var refreshed = await RefreshAsync(client, original);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var rotated = RefreshCookieFrom(refreshed);
        Assert.NotEqual(original, rotated);

        // Um atacante reapresenta o token antigo...
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, original)).StatusCode);

        // ...e a sessão legítima também é encerrada por segurança.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, rotated)).StatusCode);
    }

    [Fact]
    public async Task Logout_revoga_o_refresh_token()
    {
        var (email, password) = await CreateActiveUserAsync(Roles.Consulta);
        using var client = factory.CreateHttpsClient(handleCookies: false);
        var cookie = RefreshCookieFrom(await client.LoginAsync(email, password));

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("Cookie", $"smartstock_refresh={cookie}");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(logout)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, cookie)).StatusCode);
    }

    [Fact]
    public async Task Cookie_de_refresh_e_HttpOnly_Secure_e_SameSite_Strict()
    {
        var (email, password) = await CreateActiveUserAsync(Roles.Consulta);
        using var client = factory.CreateHttpsClient(handleCookies: false);

        var response = await client.LoginAsync(email, password);
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("smartstock_refresh="));

        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Esqueci_senha_nao_revela_se_o_email_existe()
    {
        using var client = factory.CreateHttpsClient();
        var before = factory.Emails.Sent.Count;

        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "ninguem@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before, factory.Emails.Sent.Count);
    }

    [Fact]
    public async Task Recuperacao_de_senha_troca_a_senha()
    {
        var (email, oldPassword) = await CreateActiveUserAsync(Roles.Consulta);
        using var client = factory.CreateHttpsClient();

        (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        var link = factory.Emails.LastLinkFor(email);
        Assert.Equal("recuperacao", link.Type);
        (await client.DefinePasswordAsync(link, "NovaSenhaSegura1")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(email, oldPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, "NovaSenhaSegura1")).StatusCode);
    }

    [Fact]
    public async Task Rotas_protegidas_exigem_login()
    {
        using var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
    }

    private Task<(string Email, Guid Id)> CreateUserAsync(string role) => factory.CreateUserAsync(role);

    private Task<(string Email, string Password)> CreateActiveUserAsync(string role) => factory.CreateActiveUserAsync(role);

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"smartstock_refresh={refreshCookie}");
        return client.SendAsync(request);
    }

    private static string RefreshCookieFrom(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("smartstock_refresh="));
        return header["smartstock_refresh=".Length..header.IndexOf(';')];
    }
}
