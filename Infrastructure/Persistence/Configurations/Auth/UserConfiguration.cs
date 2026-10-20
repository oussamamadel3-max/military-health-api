using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", "iam");
        builder.HasMany(u => u.UserPermissions)
            .WithOne(up => up.User)
            .HasForeignKey(up => up.UserId);

        builder.Property(u => u.FullName)
            .HasMaxLength(250);

        builder.Property(u => u.LastLoginAt).HasColumnType("timestamptz");
        builder.Property(u => u.UpdatedAt).HasColumnType("timestamptz");

        builder.Property(u => u.MustChangePassword)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(u => u.LoginLockoutTier)
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasIndex(u => u.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("IX_Users_NormalizedUserName")
            .HasFilter("\"NormalizedUserName\" IS NOT NULL");
    }
}
