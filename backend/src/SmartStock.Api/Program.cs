using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartStock.Api.Cli;
using SmartStock.Api.Security;
using SmartStock.Application.Abstractions;
using SmartStock.Domain.Users;
using SmartStock.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
var isCommandLine = ImportCommand.IsRequested(args) || AnalyzeCommand.IsRequested(args);

builder.Services.AddHttpContextAccessor();
if (isCommandLine)
    builder.Services.AddScoped<ICurrentUser, CommandLineCurrentUser>();
else
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

AddAuthentication(builder);
AddRateLimiting(builder.Services, builder.Configuration.GetValue("RateLimiting:AuthPermitPerMinute", 10));

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
        new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState)
        {
            Title = "Dados inválidos. Verifique os campos informados.",
            Status = StatusCodes.Status400BadRequest
        }));

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(AddSecurityHeaders);

if (!app.Environment.IsDevelopment())
    app.UseHsts();

// Em produção a API também entrega a interface compilada (pasta wwwroot).
app.UseDefaultFiles();
app.UseStaticFiles();
// Roteamento depois dos arquivos estáticos: se viesse antes, a rota de fallback (index.html) seria escolhida
// para /assets/*.js e o middleware de arquivos estáticos ignoraria o pedido.
app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/api/health").AllowAnonymous();
// Rotas da interface (ex.: /usuarios) devolvem o index.html; rotas /api desconhecidas continuam 404.
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html").AllowAnonymous();

if (!app.Environment.IsEnvironment("Testing"))
{
    await app.Services.InitializeDatabaseAsync(
        applyMigrations: app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false));
}

if (isCommandLine)
{
    Environment.ExitCode = AnalyzeCommand.IsRequested(args)
        ? await AnalyzeCommand.RunAsync(app.Services)
        : await ImportCommand.RunAsync(app.Services, args);
    return;
}

app.Run();

static void AddAuthentication(WebApplicationBuilder builder)
{
    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = JwtRegisteredClaimNames.Name,
                RoleClaimType = "role"
            };
        });

    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build())
        .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Administrador))
        .AddPolicy(Policies.CanImport, p => p.RequireRole(Roles.Administrador, Roles.Operador))
        .AddPolicy(Policies.CanAnalyze, p => p.RequireRole(Roles.Administrador, Roles.Gerente, Roles.Operador))
        .AddPolicy(Policies.CanApprove, p => p.RequireRole(Roles.Administrador, Roles.Gerente));
}

static void AddRateLimiting(IServiceCollection services, int permitPerMinute) =>
    services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(Policies.AuthRateLimit, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1) }));
    });

static Task AddSecurityHeaders(HttpContext context, Func<Task> next)
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    return next();
}

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
