using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Analysis;
using SmartStock.Application.Assistant;
using SmartStock.Application.Auditing;
using SmartStock.Application.Auth;
using SmartStock.Application.Catalog;
using SmartStock.Application.Imports;
using SmartStock.Application.Inventory;
using SmartStock.Application.Reports;
using SmartStock.Application.Users;
using SmartStock.Infrastructure.Analysis;
using SmartStock.Infrastructure.Assistant;
using SmartStock.Infrastructure.Auditing;
using SmartStock.Infrastructure.Auth;
using SmartStock.Infrastructure.Catalog;
using SmartStock.Infrastructure.Imports;
using SmartStock.Infrastructure.Imports.Definitions;
using SmartStock.Infrastructure.Inventory;
using SmartStock.Infrastructure.Email;
using SmartStock.Infrastructure.Identity;
using SmartStock.Infrastructure.Persistence;
using SmartStock.Infrastructure.Reports;
using SmartStock.Infrastructure.Seeding;
using SmartStock.Infrastructure.Users;

namespace SmartStock.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        AddOptions(services);
        AddPersistence(services, configuration);
        AddIdentity(services, configuration);
        AddEmail(services, configuration, environment);

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<SessionTokenService>();
        services.AddScoped<PasswordLinkSender>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IdentitySeeder>();

        services.AddScoped<CatalogSeeder>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IImportDefinition, BrandImportDefinition>();
        services.AddScoped<IImportDefinition, ProductImportDefinition>();
        services.AddScoped<IImportDefinition, StockImportDefinition>();
        services.AddScoped<IImportDefinition, DailySalesImportDefinition>();
        services.AddScoped<IImportDefinition, SalesImportDefinition>();
        services.AddScoped<IImportDefinition, TransferImportDefinition>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IInventoryQueryService, InventoryQueryService>();
        services.AddScoped<NegativeStockCalculator>();
        services.AddScoped<CompletedTransferMatcher>();
        services.AddScoped<AlertCalculator>();
        services.AddScoped<AlertNotifier>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IStockAnalysisService, StockAnalysisService>();
        services.AddScoped<IAnalysisQueryService, AnalysisQueryService>();
        services.AddScoped<INegativeStockQueryService, NegativeStockQueryService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAssistantService, AssistantService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IMonthlyReportService, MonthlyReportService>();
        services.AddScoped<IDailySalesReportService, DailySalesReportService>();

        return services;
    }

    /// <summary>Aplica as migrações (se habilitado) e garante perfis e Administrador inicial.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider, bool applyMigrations)
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        if (applyMigrations)
            await scope.ServiceProvider.GetRequiredService<SmartStockDbContext>().Database.MigrateAsync();

        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
        await scope.ServiceProvider.GetRequiredService<CatalogSeeder>().SeedAsync();
    }

    private static void AddOptions(IServiceCollection services)
    {
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AccountOptions>().BindConfiguration(AccountOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AppOptions>().BindConfiguration(AppOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ImportOptions>().BindConfiguration(ImportOptions.SectionName);
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' não configurada. Veja 05 - Desenvolvimento/README.md, seção \"Segredos\".");

        services.AddDbContext<SmartStockDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        // Chaves de proteção no banco: links de convite/recuperação continuam válidos após reiniciar o servidor.
        services.AddDataProtection()
            .SetApplicationName("SmartStock")
            .PersistKeysToDbContext<SmartStockDbContext>();
    }

    private static void AddIdentity(IServiceCollection services, IConfiguration configuration)
    {
        var accounts = configuration.GetSection(AccountOptions.SectionName).Get<AccountOptions>() ?? new AccountOptions();

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<SmartStockDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<InviteTokenProvider>(InviteTokenProvider.ProviderName)
            .AddErrorDescriber<PortugueseIdentityErrorDescriber>();

        services.Configure<DataProtectionTokenProviderOptions>(o =>
            o.TokenLifespan = TimeSpan.FromHours(accounts.ResetLinkHours));
        services.Configure<InviteTokenProviderOptions>(o =>
            o.TokenLifespan = TimeSpan.FromHours(accounts.InviteLinkHours));
    }

    private static void AddEmail(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var mode = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()?.Mode ?? EmailDeliveryMode.File;

        if (mode == EmailDeliveryMode.File && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("O modo de e-mail 'File' só pode ser usado em desenvolvimento.");

        if (mode == EmailDeliveryMode.Smtp)
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        else
            services.AddScoped<IEmailSender, FileEmailSender>();
    }
}
