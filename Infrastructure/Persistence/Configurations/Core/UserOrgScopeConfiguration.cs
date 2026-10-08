using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Core;

public class UserOrgScopeConfiguration : IEntityTypeConfiguration<UserOrgScope>
{
    public void Configure(EntityTypeBuilder<UserOrgScope> builder)
    {
        builder.ToTable("UserOrgScope");
        builder.HasKey(x => x.UserOrgScopeId);
        builder.Property(x => x.ScopeMode).IsRequired();
        builder.Property(x => x.Status).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.PermissionId, x.OrgUnitId, x.ScopeMode }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.PermissionId, x.Status });
        builder.HasIndex(x => new { x.OrgUnitId, x.Status });
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OrgUnit).WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AssignedByUser).WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
