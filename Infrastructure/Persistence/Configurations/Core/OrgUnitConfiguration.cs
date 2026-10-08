using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Core;

public class OrgUnitConfiguration : IEntityTypeConfiguration<OrgUnit>
{
    public void Configure(EntityTypeBuilder<OrgUnit> builder)
    {
        builder.ToTable("OrgUnit");
        builder.HasKey(x => x.OrgUnitId);
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.NameAr).IsRequired();
        builder.Property(x => x.Status).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.ParentOrgUnitId, x.NameAr }).IsUnique().HasFilter("\"Status\" <> 'ARCHIVED'");
        builder.HasIndex(x => x.ParentOrgUnitId);
        builder.HasIndex(x => new { x.OrgUnitTypeId, x.Status });
        builder.HasOne(x => x.OrgUnitType).WithMany().HasForeignKey(x => x.OrgUnitTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ParentOrgUnit).WithMany().HasForeignKey(x => x.ParentOrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
