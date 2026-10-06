using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Core;

public class OrgUnitTypeConfiguration : IEntityTypeConfiguration<OrgUnitType>
{
    public void Configure(EntityTypeBuilder<OrgUnitType> builder)
    {
        builder.ToTable("OrgUnitType");
        builder.HasKey(x => x.OrgUnitTypeId);
        builder.Property(x => x.Code).HasColumnType("citext").IsRequired();
        builder.Property(x => x.NameAr).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}
