using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using SmartStock.Application.Abstractions;
using SmartStock.Infrastructure;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Tests.Infrastructure;

/// <summary>
/// Sobe a API inteira em memória contra o banco local "smartstock_tests" (recriado a cada execução).
/// E-mails não são enviados: ficam em <see cref="Emails"/> para os testes lerem os links.
/// </summary>
public sealed class SmartStockApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "SenhaAdmin123";

    public CapturingEmailSender Emails { get; } = new();

    /// <summary>Arquivos enviados nos testes ficam numa pasta temporária, apagada no fim.</summary>
    public string ImportStoragePath { get; } = Path.Combine(Path.GetTempPath(), "smartstock-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Interface compilada falsa (index.html + um asset), como fica em produção na pasta wwwroot.</summary>
    public string WebRootPath { get; } = Path.Combine(Path.GetTempPath(), "smartstock-tests-wwwroot-" + Guid.NewGuid().ToString("N"));

    public const string FakeAssetPath = "/assets/app-teste.js";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        CreateFakeWebRoot();
        builder.UseWebRoot(WebRootPath);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", TestConnectionString());
        builder.UseSetting("App:PublicUrl", "http://localhost:5173");
        builder.UseSetting("Jwt:SigningKey", "chave-de-teste-com-mais-de-trinta-e-dois-caracteres");
        builder.UseSetting("Accounts:AllowedEmailDomains:0", "example.com");
        builder.UseSetting("Accounts:InitialAdminEmail", AdminEmail);
        builder.UseSetting("Accounts:InitialAdminName", "Administrador Teste");
        builder.UseSetting("Email:Mode", "File");
        builder.UseSetting("Email:FromAddress", "smartstock@example.com");
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "1000");
        builder.UseSetting("Imports:StoragePath", ImportStoragePath);

        builder.ConfigureLogging(logging => logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.Error));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public async Task InitializeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartStockDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
        }

        await Services.InitializeDatabaseAsync(applyMigrations: false);
        await ActivateAdminAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(ImportStoragePath))
            Directory.Delete(ImportStoragePath, recursive: true);
        if (Directory.Exists(WebRootPath))
            Directory.Delete(WebRootPath, recursive: true);
    }

    private void CreateFakeWebRoot()
    {
        Directory.CreateDirectory(Path.Combine(WebRootPath, "assets"));
        File.WriteAllText(Path.Combine(WebRootPath, "index.html"), "<!doctype html><div id=\"root\"></div>");
        File.WriteAllText(Path.Combine(WebRootPath, FakeAssetPath.TrimStart('/')), "console.log('ok')");
    }

    /// <summary>Cliente com endereço https, necessário para o cookie Secure do refresh token.</summary>
    public HttpClient CreateHttpsClient(bool handleCookies = true) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies
        });

    private async Task ActivateAdminAsync()
    {
        var link = Emails.LastLinkFor(AdminEmail);
        using var client = CreateHttpsClient();
        var response = await client.DefinePasswordAsync(link, AdminPassword);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Falha ao ativar o Administrador ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Usa a mesma connection string gravada pelo script configurar-ambiente-dev.ps1 (user-secrets),
    /// trocando o banco para "smartstock_tests". A variável SMARTSTOCK_TEST_CONNECTION tem prioridade.
    /// </summary>
    private static string TestConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTSTOCK_TEST_CONNECTION")
            ?? new ConfigurationBuilder().AddUserSecrets<Program>().Build().GetConnectionString("Default");

        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "Banco de testes não configurado. Rode scripts/configurar-ambiente-dev.ps1 antes dos testes.");

        return new NpgsqlConnectionStringBuilder(configured) { Database = "smartstock_tests" }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<SmartStockApiFactory>
{
    public const string Name = "API";
}
