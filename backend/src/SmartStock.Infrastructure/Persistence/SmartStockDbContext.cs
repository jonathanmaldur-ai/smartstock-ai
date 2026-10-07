using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Identity;

namespace SmartStock.Infrastructure.Persistence;

public class SmartStockDbContext(DbContextOptions<SmartStockDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryAlias> CategoryAliases => Set<CategoryAlias>();
    public DbSet<Subcategory> Subcategories => Set<Subcategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportIssue> ImportIssues => Set<ImportIssue>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<SalesTotal> SalesTotals => Set<SalesTotal>();
    public DbSet<TransferMovement> TransferMovements => Set<TransferMovement>();
    public DbSet<DailySale> DailySales => Set<DailySale>();
    public DbSet<DailySaleItem> DailySaleItems => Set<DailySaleItem>();
    public DbSet<StoreSale> StoreSales => Set<StoreSale>();
    public DbSet<StoreSaleLine> StoreSaleLines => Set<StoreSaleLine>();
    public DbSet<StockParameters> StockParameters => Set<StockParameters>();
    public DbSet<StockAnalysis> StockAnalyses => Set<StockAnalysis>();
    public DbSet<StockPosition> StockPositions => Set<StockPosition>();
    public DbSet<TransferSuggestion> TransferSuggestions => Set<TransferSuggestion>();
    public DbSet<PurchaseSuggestion> PurchaseSuggestions => Set<PurchaseSuggestion>();
    public DbSet<NegativeStock> NegativeStocks => Set<NegativeStock>();
    public DbSet<StockAlert> StockAlerts => Set<StockAlert>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        RenameIdentityTables(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(SmartStockDbContext).Assembly);
    }

    private static void RenameIdentityTables(ModelBuilder builder)
    {
        builder.Entity<AppUser>().ToTable("users");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
    }
}
