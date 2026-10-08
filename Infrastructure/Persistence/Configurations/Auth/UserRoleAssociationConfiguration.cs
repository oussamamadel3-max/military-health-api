using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class UserRoleAssociationConfiguration : IEntityTypeConfiguration<UserRoleAssociation>
{
    public void Configure(EntityTypeBuilder<UserRoleAssociation> builder)
    {
        builder.HasKey(uur => uur.Id);

        builder.HasOne(uur => uur.User)
            .WithMany(u => u.UserRoleAssociations)
            .HasForeignKey(uur => uur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(uur => uur.Role)
            .WithMany(r => r.UserRoleAssociations)
            .HasForeignKey(uur => uur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
