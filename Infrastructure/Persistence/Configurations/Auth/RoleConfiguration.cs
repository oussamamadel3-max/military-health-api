using Domain.Entities.Auth;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", "iam");
        builder.Property(r => r.Scope)
            .HasConversion<int>()
            .HasDefaultValue(RoleScope.Center)
            .IsRequired();
        builder.HasIndex(r => r.Scope);
        builder.HasMany(r => r.RolePermissions)
            .WithOne(rp => rp.Role)
            .HasForeignKey(rp => rp.RoleId);
    }
}
