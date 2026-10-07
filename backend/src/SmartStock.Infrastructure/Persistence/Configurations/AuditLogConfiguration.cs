using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartStock.Domain.Auditing;

namespace SmartStock.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();

        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.UserEmail).HasMaxLength(256);
        builder.Property(a => a.EntityType).HasMaxLength(100);
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.Details).HasColumnType("jsonb");
        builder.Property(a => a.Result).HasConversion<string>().HasMaxLength(20);

        // Sem chave estrangeira para users de propósito: o log deve sobreviver a qualquer alteração de usuário.
        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => a.Action);
        builder.HasIndex(a => a.UserId);
    }
}
