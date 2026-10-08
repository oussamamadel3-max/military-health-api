using Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Auth;

public class UserCenterConfiguration : IEntityTypeConfiguration<UserCenter>
{
    public void Configure(EntityTypeBuilder<UserCenter> builder)
    {
        builder.ToTable("userCenter", "iam");
        builder.HasKey(x => new { x.UserId, x.CenterId });
        builder.UseCamelCaseColumns();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => new { x.CenterId, x.IsActive });
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.AssignedByUserId).IsRequired();
        builder.HasOne(x => x.AssignedByUser).WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RevokedByUser).WithMany().HasForeignKey(x => x.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckUserCenter1", "(\"isActive\" = true AND \"revokedAt\" IS NULL AND \"revokedByUserId\" IS NULL AND (\"revokeReason\" IS NULL OR btrim(\"revokeReason\") = '')) OR (\"isActive\" = false AND \"revokedAt\" IS NOT NULL AND \"revokedByUserId\" IS NOT NULL AND \"revokeReason\" IS NOT NULL AND btrim(\"revokeReason\") <> '')");
            t.HasCheckConstraint("ckUserCenter2", "\"revokedAt\" IS NULL OR \"revokedAt\" >= \"assignedAt\"");
        });
    }
}

public class UserClinicAssignmentConfiguration : IEntityTypeConfiguration<UserClinicAssignment>
{
    public void Configure(EntityTypeBuilder<UserClinicAssignment> builder)
    {
        builder.ToTable("userClinicAssignment", "iam");
        builder.HasKey(x => x.AssignmentId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => new { x.CenterId, x.ClinicId, x.IsActive });
        builder.HasIndex(x => new { x.UserId, x.CenterId, x.ClinicId }).IsUnique().HasFilter("\"isActive\" = true");
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.AssignedByUserId).IsRequired();
        builder.HasOne(x => x.AssignedByUser).WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RevokedByUser).WithMany().HasForeignKey(x => x.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckUserClinicAssignment1", "(\"isActive\" = true AND \"revokedAt\" IS NULL AND \"revokedByUserId\" IS NULL AND (\"revokeReason\" IS NULL OR btrim(\"revokeReason\") = '')) OR (\"isActive\" = false AND \"revokedAt\" IS NOT NULL AND \"revokedByUserId\" IS NOT NULL AND \"revokeReason\" IS NOT NULL AND btrim(\"revokeReason\") <> '')");
            t.HasCheckConstraint("ckUserClinicAssignment2", "\"revokedAt\" IS NULL OR \"revokedAt\" >= \"assignedAt\"");
        });
        builder.HasOne<UserCenter>().WithMany().HasForeignKey(x => new { x.UserId, x.CenterId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Entities.Core.CenterClinic>().WithMany()
            .HasForeignKey(x => new { x.CenterId, x.ClinicId })
            .HasPrincipalKey(x => new { x.CenterId, x.ClinicId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PasswordPolicyConfiguration : IEntityTypeConfiguration<PasswordPolicy>
{
    public void Configure(EntityTypeBuilder<PasswordPolicy> builder)
    {
        builder.ToTable("passwordPolicy", "iam");
        builder.HasKey(x => x.PasswordPolicyId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.HistoryDepth).HasDefaultValue((short)0);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.VersionNo).IsUnique();
        builder.HasIndex(x => x.IsActive).IsUnique().HasFilter("\"isActive\" = true");
        builder.HasIndex(x => new { x.IsActive, x.ValidFrom });
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckPasswordPolicy1", "\"validTo\" IS NULL OR \"validTo\" > \"validFrom\"");
            t.HasCheckConstraint("ckPasswordPolicy2", "\"versionNo\" > 0");
            t.HasCheckConstraint("ckPasswordPolicy3", "\"minLength\" > 0");
            t.HasCheckConstraint("ckPasswordPolicy4", "\"historyDepth\" >= 0");
            t.HasCheckConstraint("ckPasswordPolicyLengthRange", "\"maxLength\" >= \"minLength\"");
            t.HasCheckConstraint("ckPasswordPolicyMaxLengthPositive", "\"maxLength\" > 0");
        });
    }
}

public class UserPasswordHistoryConfiguration : IEntityTypeConfiguration<UserPasswordHistory>
{
    public void Configure(EntityTypeBuilder<UserPasswordHistory> builder)
    {
        builder.ToTable("userPasswordHistory", "iam");
        builder.HasKey(x => x.PasswordHistoryId);
        builder.UseCamelCaseColumns();
        builder.HasIndex(x => new { x.UserId, x.SetAt });
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PasswordPolicy).WithMany().HasForeignKey(x => x.PasswordPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckUserPasswordHistory1", "btrim(\"passwordHash\") <> ''"));
    }
}

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("userProfile", "iam");
        builder.HasKey(x => x.UserId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.EmployeeNumber).HasColumnType("citext").IsRequired();
        builder.Property(x => x.RankOrTitle).IsRequired();
        builder.Property(x => x.UpdatedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
        builder.HasIndex(x => x.EmployeeNumber).IsUnique();
        builder.HasOne(x => x.User).WithOne(x => x.Profile).HasForeignKey<UserProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProfileFileAsset).WithMany().HasForeignKey(x => x.ProfileFileAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckUserProfile1", "btrim(\"rankOrTitle\") <> ''"));
    }
}

public class UserContactConfiguration : IEntityTypeConfiguration<UserContact>
{
    public void Configure(EntityTypeBuilder<UserContact> builder)
    {
        builder.ToTable("userContact", "iam");
        builder.HasKey(x => x.ContactId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.ContactType).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ContactValue).IsRequired();
        builder.Property(x => x.IsPrimary).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
        builder.HasIndex(x => new { x.UserId, x.ContactType, x.ContactValue }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.ContactType }).IsUnique().HasFilter("\"isPrimary\" = true AND \"isActive\" = true");
        builder.HasIndex(x => new { x.UserId, x.IsActive });
        builder.HasOne(x => x.User).WithMany(x => x.Contacts).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckUserContact1", "\"contactType\" IN ('PHONE','EMAIL','OTHER')");
            t.HasCheckConstraint("ckUserContact2", "btrim(\"contactValue\") <> ''");
        });
    }
}

public class FieldPermissionConfiguration : IEntityTypeConfiguration<FieldPermission>
{
    public void Configure(EntityTypeBuilder<FieldPermission> builder)
    {
        builder.ToTable("FieldPermissions", "iam");
        builder.HasKey(x => x.Id);
    }
}
