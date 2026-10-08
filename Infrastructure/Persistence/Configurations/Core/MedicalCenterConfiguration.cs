using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Core;

public class MedicalCenterConfiguration : IEntityTypeConfiguration<MedicalCenter>
{
    public void Configure(EntityTypeBuilder<MedicalCenter> builder)
    {
        builder.ToTable("medicalCenter", "core");
        builder.HasKey(x => x.CenterId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.Code).HasColumnType("citext").IsRequired();
        builder.Property(x => x.NameAr).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IsActive);
        builder.ToTable(t => t.HasCheckConstraint(
            "ckMedicalCenterDeactivation",
            "(\"isActive\" = true AND \"deactivationReason\" IS NULL) OR (\"isActive\" = false AND \"deactivationReason\" IS NOT NULL AND btrim(\"deactivationReason\") <> '')"));
    }
}
