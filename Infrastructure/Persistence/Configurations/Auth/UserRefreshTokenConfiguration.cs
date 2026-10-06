using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    public void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        builder.ToTable("refreshToken", "iam");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("refreshTokenId");
        builder.Property(e => e.UserId).HasColumnName("userId").IsRequired();
        builder.Property(e => e.Token).HasColumnName("tokenHash").IsRequired();
        builder.Property(e => e.TokenFamilyId).HasColumnName("tokenFamilyId");
        builder.Property(e => e.ParentRefreshTokenId).HasColumnName("parentRefreshTokenId");
        builder.Property(e => e.SecurityStamp).HasColumnName("securityStamp");
        builder.Property(e => e.IssuedAt).HasColumnName("issuedAt").HasColumnType("timestamptz");
        builder.Property(e => e.ExpiryDate).HasColumnName("expiresAt").IsRequired();
        builder.Property(e => e.IsRevoked).HasColumnName("isRevoked").HasDefaultValue(false);
        builder.Property(e => e.RevokedAt).HasColumnName("revokedAt").HasColumnType("timestamptz");
        builder.Property(e => e.RevokeReason).HasColumnName("revokeReason");
        builder.Property(e => e.ReplacedByRefreshTokenId).HasColumnName("replacedByRefreshTokenId");

        builder.HasIndex(e => e.Token).IsUnique();
        builder.HasIndex(e => new { e.UserId, e.ExpiryDate });
        builder.HasIndex(e => new { e.TokenFamilyId, e.IssuedAt });
        builder.HasIndex(e => e.ParentRefreshTokenId).IsUnique().HasFilter("\"parentRefreshTokenId\" IS NOT NULL");

        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.ParentRefreshToken).WithMany().HasForeignKey(e => e.ParentRefreshTokenId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ReplacedByRefreshToken).WithMany().HasForeignKey(e => e.ReplacedByRefreshTokenId).OnDelete(DeleteBehavior.Restrict);
    }
}
