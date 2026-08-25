using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLog");

        builder.HasKey(a => a.LogId);

        builder.Property(a => a.ActionType)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(a => a.TableName)
            .IsRequired()
            .HasMaxLength(100)
            .IsUnicode(false);

        builder.Property(a => a.ChangeSummary)
            .HasMaxLength(1000);

        builder.Property(a => a.Timestamp)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(a => new { a.TableName, a.RecordId })
            .HasDatabaseName("idx_table_record");

        builder.HasIndex(a => a.Timestamp)
            .HasDatabaseName("idx_timestamp");
    }
}
