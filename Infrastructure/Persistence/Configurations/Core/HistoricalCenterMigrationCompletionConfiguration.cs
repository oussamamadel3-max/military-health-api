using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Core;

public class HistoricalCenterMigrationCompletionConfiguration : IEntityTypeConfiguration<HistoricalCenterMigrationCompletion>
{
    public void Configure(EntityTypeBuilder<HistoricalCenterMigrationCompletion> builder)
    {
        builder.ToTable("historicalCenterMigrationCompletion", "core");
        builder.HasKey(x => x.Id);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.VerifiedByUserId).IsRequired();
        builder.Property(x => x.SummaryJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.VerifiedAt).HasColumnType("timestamptz");
        builder.HasIndex(x => x.VerifiedAt);
        builder.HasOne(x => x.TargetCenter)
            .WithMany()
            .HasForeignKey(x => x.TargetCenterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
