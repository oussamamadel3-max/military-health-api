using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasMany(u => u.UserPermissions)
            .WithOne(up => up.User)
            .HasForeignKey(up => up.UserId);

        builder.Property(u => u.FullName)
            .HasMaxLength(250);

        builder.Property(u => u.ProfilePhotoPath)
            .HasMaxLength(500);

        builder.Property(u => u.LoginLockoutTier)
            .HasDefaultValue(0)
            .IsRequired();
    }
}
