using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartStock.Infrastructure.Persistence;

/// <summary>
/// Usado apenas pelo "dotnet ef" para gerar migrações. Não conecta no banco,
/// por isso não precisa da senha real.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmartStockDbContext>
{
    public SmartStockDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SmartStockDbContext>()
            .UseNpgsql("Host=localhost;Database=smartstock_design")
            .UseSnakeCaseNamingConvention()
            .Options);
}
