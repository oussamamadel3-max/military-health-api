using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class UserFieldPermissionConfiguration : IEntityTypeConfiguration<UserFieldPermission>
{
    public void Configure(EntityTypeBuilder<UserFieldPermission> builder)
    {
        builder.ToTable("UserFieldPermissions", "iam");
        builder.HasKey(ufp => ufp.Id);

        builder.HasOne(ufp => ufp.User)
            .WithMany(u => u.UserFieldPermissions)
            .HasForeignKey(ufp => ufp.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ufp => ufp.FieldPermission)
            .WithMany(fp => fp.UserFieldPermissions)
            .HasForeignKey(ufp => ufp.FieldPermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
