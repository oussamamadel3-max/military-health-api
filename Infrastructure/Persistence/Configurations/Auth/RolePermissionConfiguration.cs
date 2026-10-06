using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", "iam");
        builder.HasKey(rp => rp.Id);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique().HasFilter("\"IsActive\" = true");
        builder.Property(x => x.GrantedByUserId).IsRequired();
        builder.HasOne(x => x.GrantedByUser).WithMany().HasForeignKey(x => x.GrantedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RevokedByUser).WithMany().HasForeignKey(x => x.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckRolePermission1", "(\"IsActive\" = true AND \"RevokedAt\" IS NULL AND \"RevokedByUserId\" IS NULL AND (\"RevokeReason\" IS NULL OR btrim(\"RevokeReason\") = '')) OR (\"IsActive\" = false AND \"RevokedAt\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL AND \"RevokeReason\" IS NOT NULL AND btrim(\"RevokeReason\") <> '')");
            t.HasCheckConstraint("ckRolePermission2", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"GrantedAt\"");
        });
    }
}
