using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;

namespace SmartStock.Infrastructure.Persistence.Configurations;

internal sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores");
        builder.Property(s => s.Code).HasMaxLength(3).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(s => s.IsActive);
    }
}

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");
        builder.Property(b => b.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(b => b.Code).IsUnique();
        builder.Property(b => b.Name).HasMaxLength(150).IsRequired();
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(b => b.LastImportId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
        builder.HasMany(c => c.Aliases).WithOne(a => a.Category).HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CategoryAliasConfiguration : IEntityTypeConfiguration<CategoryAlias>
{
    public void Configure(EntityTypeBuilder<CategoryAlias> builder)
    {
        builder.ToTable("category_aliases");
        builder.Property(a => a.Alias).HasMaxLength(100).IsRequired();
        builder.HasIndex(a => a.Alias).IsUnique();
    }
}

internal sealed class SubcategoryConfiguration : IEntityTypeConfiguration<Subcategory>
{
    public void Configure(EntityTypeBuilder<Subcategory> builder)
    {
        builder.ToTable("subcategories");
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.Property(p => p.Code).HasMaxLength(30).IsRequired();
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.Description).HasMaxLength(300).IsRequired();
        builder.Property(p => p.Unit).HasMaxLength(10);
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Property(p => p.FiscalDescription).HasMaxLength(300);
        // A importação de produtos não informa esta coluna: o padrão do banco evita apagar a marcação ao reimportar.
        builder.Property(p => p.IsSeasonal).HasDefaultValue(false);

        builder.HasOne(p => p.Brand).WithMany().HasForeignKey(p => p.BrandId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Subcategory).WithMany().HasForeignKey(p => p.SubcategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ImportBatch>().WithMany().HasForeignKey(p => p.LastImportId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("import_batches");
        builder.Property(b => b.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.FileName).HasMaxLength(260).IsRequired();
        builder.Property(b => b.FileHash).HasMaxLength(64).IsRequired();
        builder.Property(b => b.StoredFilePath).HasMaxLength(500).IsRequired();
        builder.Property(b => b.UploadedByEmail).HasMaxLength(256).IsRequired();
        builder.Property(b => b.DecidedByEmail).HasMaxLength(256);
        builder.Property(b => b.RejectionReason).HasMaxLength(1000);
        builder.Property(b => b.IssueSummary).HasColumnType("jsonb");
        builder.HasIndex(b => b.UploadedAt);
        builder.HasIndex(b => new { b.Type, b.FileHash });
        builder.HasIndex(b => new { b.Type, b.Status, b.ReferenceDate });
        builder.HasOne(b => b.Store).WithMany().HasForeignKey(b => b.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Issues).WithOne().HasForeignKey(i => i.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ImportIssueConfiguration : IEntityTypeConfiguration<ImportIssue>
{
    public void Configure(EntityTypeBuilder<ImportIssue> builder)
    {
        builder.ToTable("import_issues");
        builder.Property(i => i.Id).UseIdentityAlwaysColumn();
        builder.Property(i => i.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Column).HasMaxLength(100);
        builder.Property(i => i.Code).HasMaxLength(60).IsRequired();
        builder.Property(i => i.Message).HasMaxLength(500).IsRequired();
        builder.Property(i => i.Value).HasMaxLength(300);
        builder.HasIndex(i => new { i.BatchId, i.RowNumber });
    }
}
