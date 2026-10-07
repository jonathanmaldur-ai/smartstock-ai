using System.Net.Http.Json;
using System.Text.Json;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Users;
using SmartStock.Tests.Infrastructure;

namespace SmartStock.Tests.Api;

[Collection(ApiCollection.Name)]
public sealed class AuditTests(SmartStockApiFactory factory)
{
    [Fact]
    public async Task Login_com_falha_e_com_sucesso_ficam_registrados()
    {
        var (email, password) = await factory.CreateActiveUserAsync(Roles.Consulta);
        using var client = factory.CreateHttpsClient();
        await client.LoginAsync(email, "SenhaErrada99");
        await client.LoginAsync(email, password);

        using var admin = await factory.CreateAdminClientAsync();
        var page = await admin.GetFromJsonAsync<AuditPage>(
            $"/api/audit?userEmail={Uri.EscapeDataString(email)}&pageSize=100", ApiClientExtensions.Json);

        Assert.Contains(page!.Items, a => a.Action == AuditActions.LoginFailed && a.Result == AuditResult.Failure);
        Assert.Contains(page.Items, a => a.Action == AuditActions.LoginSucceeded && a.Result == AuditResult.Success);
    }

    [Fact]
    public async Task Criacao_de_usuario_registra_quem_criou_e_os_dados()
    {
        var (email, _) = await factory.CreateUserAsync(Roles.Operador);

        using var admin = await factory.CreateAdminClientAsync();
        var page = await admin.GetFromJsonAsync<AuditPage>(
            $"/api/audit?action={AuditActions.UserCreated}&pageSize=100", ApiClientExtensions.Json);

        var entry = page!.Items.Single(a => a.Details is not null && a.Details.Contains(email));
        Assert.Equal(SmartStockApiFactory.AdminEmail, entry.UserEmail);
        Assert.Equal(Roles.Operador, JsonDocument.Parse(entry.Details!).RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Paginacao_respeita_o_limite_maximo()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var page = await admin.GetFromJsonAsync<AuditPage>("/api/audit?pageSize=5000", ApiClientExtensions.Json);

        Assert.Equal(100, page!.PageSize);
    }

    private sealed record AuditPage(List<AuditItem> Items, int Page, int PageSize, int TotalCount);

    private sealed record AuditItem(
        long Id, DateTimeOffset OccurredAt, string? UserEmail, string Action,
        string? EntityType, string? EntityId, AuditResult Result, string? Details, string? IpAddress);
}
