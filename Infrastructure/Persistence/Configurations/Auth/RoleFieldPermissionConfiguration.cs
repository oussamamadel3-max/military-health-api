using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class RoleFieldPermissionConfiguration : IEntityTypeConfiguration<RoleFieldPermission>
{
    public void Configure(EntityTypeBuilder<RoleFieldPermission> builder)
    {
        builder.HasKey(rfp => rfp.Id);

        builder.HasOne(rfp => rfp.Role)
            .WithMany(r => r.RoleFieldPermissions)
            .HasForeignKey(rfp => rfp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rfp => rfp.FieldPermission)
            .WithMany(fp => fp.RoleFieldPermissions)
            .HasForeignKey(rfp => rfp.FieldPermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
