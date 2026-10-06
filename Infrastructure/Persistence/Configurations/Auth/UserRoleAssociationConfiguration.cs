using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class UserRoleAssociationConfiguration : IEntityTypeConfiguration<UserRoleAssociation>
{
    public void Configure(EntityTypeBuilder<UserRoleAssociation> builder)
    {
        builder.ToTable("UserRoleAssociations", "iam");
        builder.HasKey(uur => uur.Id);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(uur => uur.User)
            .WithMany(u => u.UserRoleAssociations)
            .HasForeignKey(uur => uur.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(uur => uur.Role)
            .WithMany(r => r.UserRoleAssociations)
            .HasForeignKey(uur => uur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AssignedByUser).WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RevokedByUser).WithMany().HasForeignKey(x => x.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.UserId, x.RoleId, x.CenterId }).IsUnique().HasFilter("\"CenterId\" IS NOT NULL AND \"IsActive\" = true");
        builder.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique().HasFilter("\"CenterId\" IS NULL AND \"IsActive\" = true");
        builder.HasIndex(x => new { x.UserId, x.CenterId, x.IsActive });
        builder.Property(x => x.AssignedByUserId).IsRequired();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckUserRoleAssignment1", "(\"IsActive\" = true AND \"RevokedAt\" IS NULL AND \"RevokedByUserId\" IS NULL AND (\"RevokeReason\" IS NULL OR btrim(\"RevokeReason\") = '')) OR (\"IsActive\" = false AND \"RevokedAt\" IS NOT NULL AND \"RevokedByUserId\" IS NOT NULL AND \"RevokeReason\" IS NOT NULL AND btrim(\"RevokeReason\") <> '')");
            t.HasCheckConstraint("ckUserRoleAssignment2", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"AssignedAt\"");
        });
    }
}
