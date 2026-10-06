using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("UserPermissions", "iam");
        builder.HasKey(up => up.Id);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(up => up.User)
            .WithMany(u => u.UserPermissions)
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(up => up.Permission)
            .WithMany(p => p.UserPermissions)
            .HasForeignKey(up => up.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.GrantedByUser).WithMany().HasForeignKey(x => x.GrantedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RevokedByUser).WithMany().HasForeignKey(x => x.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.UserId, x.PermissionId, x.CenterId }).IsUnique().HasFilter("\"CenterId\" IS NOT NULL AND \"IsActive\" = true");
        builder.HasIndex(x => new { x.UserId, x.PermissionId }).IsUnique().HasFilter("\"CenterId\" IS NULL AND \"IsActive\" = true");
        builder.HasIndex(x => new { x.UserId, x.CenterId, x.IsActive });
        builder.Property(x => x.PermissionId).IsRequired();
        builder.Property(x => x.GrantedByUserId).IsRequired();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckUserPermissionAssignment1", "(\"IsActive\" = true AND \"RevokedAt\" IS NULL AND \"RevokedByUserId\" IS NULL AND (\"RevokeReason\" IS NULL OR btrim(\"RevokeReason\") = '')) OR (\"IsActive\" = false AND \"RevokedAt\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL AND \"RevokeReason\" IS NOT NULL AND btrim(\"RevokeReason\") <> '')");
            t.HasCheckConstraint("ckUserPermissionAssignment2", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"GrantedAt\"");
        });
    }
}
