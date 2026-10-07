using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;
using SmartStock.Domain.Inventory;

namespace SmartStock.Infrastructure.Persistence.Configurations;

internal sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.ToTable("stock_levels");
        builder.HasKey(s => new { s.ImportId, s.ProductId, s.StoreId });
        builder.Property(s => s.Quantity).HasPrecision(14, 4);
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(s => s.ImportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesTotalConfiguration : IEntityTypeConfiguration<SalesTotal>
{
    public void Configure(EntityTypeBuilder<SalesTotal> builder)
    {
        builder.ToTable("sales_totals");
        builder.HasKey(s => new { s.ImportId, s.ProductId });
        builder.Property(s => s.Quantity).HasPrecision(14, 4);
        builder.Property(s => s.Amount).HasPrecision(14, 2);
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(s => s.ImportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransferMovementConfiguration : IEntityTypeConfiguration<TransferMovement>
{
    public void Configure(EntityTypeBuilder<TransferMovement> builder)
    {
        builder.ToTable("transfer_movements");
        builder.Property(t => t.Id).UseIdentityAlwaysColumn();
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Quantity).HasPrecision(14, 4);
        builder.Property(t => t.UserName).HasMaxLength(100);
        builder.HasIndex(t => new { t.ProductId, t.Date });
        builder.HasIndex(t => t.Date);
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(t => t.ImportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(t => t.OriginStoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(t => t.DestinationStoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DailySaleConfiguration : IEntityTypeConfiguration<DailySale>
{
    public void Configure(EntityTypeBuilder<DailySale> builder)
    {
        builder.ToTable("daily_sales");
        builder.HasKey(s => new { s.StoreId, s.Date });
        builder.Property(s => s.Pieces).HasPrecision(14, 4);
        builder.Property(s => s.Gross).HasPrecision(14, 2);
        builder.Property(s => s.Net).HasPrecision(14, 2);
        builder.HasIndex(s => s.Date);
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(s => s.ImportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DailySaleItemConfiguration : IEntityTypeConfiguration<DailySaleItem>
{
    public void Configure(EntityTypeBuilder<DailySaleItem> builder)
    {
        builder.ToTable("daily_sale_items");
        builder.HasKey(i => new { i.StoreId, i.Date, i.ProductId });
        builder.Property(i => i.Quantity).HasPrecision(14, 4);
        builder.Property(i => i.Amount).HasPrecision(14, 2);
        builder.HasIndex(i => new { i.Date, i.ProductId });
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(i => i.ImportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(i => i.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StoreSaleConfiguration : IEntityTypeConfiguration<StoreSale>
{
    public void Configure(EntityTypeBuilder<StoreSale> builder)
    {
        builder.ToTable("store_sales");
        builder.HasKey(s => new { s.StoreId, s.Number });
        builder.Property(s => s.Number).HasMaxLength(20);
        builder.Property(s => s.Gross).HasPrecision(14, 2);
        builder.Property(s => s.Net).HasPrecision(14, 2);
        builder.Property(s => s.Payment).HasMaxLength(60);
        builder.Property(s => s.FiscalDocument).HasMaxLength(80);
        builder.HasIndex(s => new { s.StoreId, s.Date });
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(s => s.ImportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Store>().WithMany().HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StoreSaleLineConfiguration : IEntityTypeConfiguration<StoreSaleLine>
{
    public void Configure(EntityTypeBuilder<StoreSaleLine> builder)
    {
        builder.ToTable("store_sale_lines");
        builder.Property(l => l.Id).UseIdentityAlwaysColumn();
        builder.Property(l => l.SaleNumber).HasMaxLength(20);
        builder.Property(l => l.Quantity).HasPrecision(14, 4);
        builder.Property(l => l.UnitPrice).HasPrecision(14, 4);
        builder.Property(l => l.Amount).HasPrecision(14, 2);
        builder.HasIndex(l => new { l.StoreId, l.Date, l.ProductId });
        builder.HasOne<StoreSale>().WithMany().HasForeignKey(l => new { l.StoreId, l.SaleNumber }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
