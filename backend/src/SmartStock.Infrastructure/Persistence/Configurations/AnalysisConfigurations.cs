using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;

namespace SmartStock.Infrastructure.Persistence.Configurations;

internal sealed class StockParametersConfiguration : IEntityTypeConfiguration<StockParameters>
{
    public void Configure(EntityTypeBuilder<StockParameters> builder)
    {
        builder.ToTable("stock_parameters");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.MinimumAnnualSales).HasPrecision(14, 4);
        builder.Property(p => p.UpdatedByEmail).HasMaxLength(256);
        builder.HasData(new StockParameters { UpdatedAt = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), UpdatedByEmail = "sistema" });
    }
}

internal sealed class StockAnalysisConfiguration : IEntityTypeConfiguration<StockAnalysis>
{
    public void Configure(EntityTypeBuilder<StockAnalysis> builder)
    {
        builder.ToTable("stock_analyses");
        builder.Property(a => a.CreatedByEmail).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Parameters).HasColumnType("jsonb");
        builder.Property(a => a.SuggestedUnits).HasPrecision(14, 4);
        builder.HasIndex(a => a.CreatedAt);
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(a => a.StockImportId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockPositionConfiguration : IEntityTypeConfiguration<StockPosition>
{
    public void Configure(EntityTypeBuilder<StockPosition> builder)
    {
        builder.ToTable("stock_positions");
        builder.HasKey(p => new { p.AnalysisId, p.ProductId, p.StoreId });
        builder.Property(p => p.Stock).HasPrecision(14, 4);
        builder.Property(p => p.ProjectedStock).HasPrecision(14, 4);
        builder.Property(p => p.Sold12Months).HasPrecision(14, 4);
        builder.Property(p => p.DailyAverage).HasPrecision(14, 6);
        builder.Property(p => p.CoverageDays).HasPrecision(14, 2);
        builder.Property(p => p.Situation).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => new { p.AnalysisId, p.Situation });
        builder.HasOne<StockAnalysis>().WithMany().HasForeignKey(p => p.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(p => p.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransferSuggestionConfiguration : IEntityTypeConfiguration<TransferSuggestion>
{
    public void Configure(EntityTypeBuilder<TransferSuggestion> builder)
    {
        builder.ToTable("transfer_suggestions");
        builder.Property(s => s.Id).UseIdentityAlwaysColumn();
        builder.Property(s => s.Quantity).HasPrecision(14, 4);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(15);
        builder.Property(s => s.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(s => s.OriginStock).HasPrecision(14, 4);
        builder.Property(s => s.OriginDailyAverage).HasPrecision(14, 6);
        builder.Property(s => s.OriginCoverageDays).HasPrecision(14, 2);
        builder.Property(s => s.DestinationStock).HasPrecision(14, 4);
        builder.Property(s => s.DestinationDailyAverage).HasPrecision(14, 6);
        builder.Property(s => s.DestinationCoverageDays).HasPrecision(14, 2);
        builder.Property(s => s.DestinationCoverageAfter).HasPrecision(14, 2);
        builder.Property(s => s.DecidedByEmail).HasMaxLength(256);
        builder.Property(s => s.DecisionNote).HasMaxLength(500);
        builder.Property(s => s.CompletedQuantity).HasPrecision(14, 4);
        builder.HasIndex(s => new { s.Status, s.AnalysisId });
        builder.HasIndex(s => new { s.ProductId, s.Status });
        builder.HasOne<StockAnalysis>().WithMany().HasForeignKey(s => s.AnalysisId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(s => s.OriginStoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(s => s.DestinationStoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NegativeStockConfiguration : IEntityTypeConfiguration<NegativeStock>
{
    public void Configure(EntityTypeBuilder<NegativeStock> builder)
    {
        builder.ToTable("negative_stocks");
        builder.HasKey(n => new { n.AnalysisId, n.ProductId, n.StoreId });
        builder.Property(n => n.Quantity).HasPrecision(14, 4);
        builder.Property(n => n.Sold12Months).HasPrecision(14, 4);
        builder.Property(n => n.PendingTransferUnits).HasPrecision(14, 4);
        builder.Property(n => n.DuplicateProductCode).HasMaxLength(30);
        builder.HasIndex(n => new { n.AnalysisId, n.StoreId });
        builder.HasOne<StockAnalysis>().WithMany().HasForeignKey(n => n.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(n => n.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(n => n.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PurchaseSuggestionConfiguration : IEntityTypeConfiguration<PurchaseSuggestion>
{
    public void Configure(EntityTypeBuilder<PurchaseSuggestion> builder)
    {
        builder.ToTable("purchase_suggestions");
        builder.Property(s => s.Id).UseIdentityAlwaysColumn();
        builder.Property(s => s.Quantity).HasPrecision(14, 4);
        builder.Property(s => s.Stock).HasPrecision(14, 4);
        builder.Property(s => s.DailyAverage).HasPrecision(14, 6);
        builder.Property(s => s.CoverageDays).HasPrecision(14, 2);
        builder.HasIndex(s => s.AnalysisId);
        builder.HasOne<StockAnalysis>().WithMany().HasForeignKey(s => s.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockAlertConfiguration : IEntityTypeConfiguration<StockAlert>
{
    public void Configure(EntityTypeBuilder<StockAlert> builder)
    {
        builder.ToTable("stock_alerts");
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Message).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.SeenByEmail).HasMaxLength(256);
        builder.Ignore(a => a.Key);
        builder.HasIndex(a => new { a.AnalysisId, a.Type });
        builder.HasOne<StockAnalysis>().WithMany().HasForeignKey(a => a.AnalysisId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(a => a.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Brand>().WithMany().HasForeignKey(a => a.BrandId).OnDelete(DeleteBehavior.Restrict);
    }
}
