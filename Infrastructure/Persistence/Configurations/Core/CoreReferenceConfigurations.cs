using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Core;

public class RecruitmentCenterConfiguration : IEntityTypeConfiguration<RecruitmentCenter>
{
    public void Configure(EntityTypeBuilder<RecruitmentCenter> builder)
    {
        builder.ToTable("recruitmentCenter", "core");
        builder.HasKey(x => x.RecruitmentCenterId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> builder)
    {
        builder.ToTable("clinic", "core");
        builder.HasKey(x => x.ClinicId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.CountsForFinal).HasDefaultValue(true);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.SortOrder).IsUnique();
    }
}

public class CenterClinicConfiguration : IEntityTypeConfiguration<CenterClinic>
{
    public void Configure(EntityTypeBuilder<CenterClinic> builder)
    {
        builder.ToTable("centerClinic", "core");
        builder.HasKey(x => x.CenterClinicId);
        builder.UseCamelCaseColumns();
        builder.HasAlternateKey(x => new { x.CenterId, x.ClinicId });
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => new { x.CenterId, x.IsActive });
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckCenterClinic1",
            "(\"isActive\" = true AND \"deactivatedAt\" IS NULL) OR (\"isActive\" = false AND \"deactivatedAt\" IS NOT NULL)"));
    }
}

public class ResultStatusConfiguration : IEntityTypeConfiguration<ResultStatus>
{
    public void Configure(EntityTypeBuilder<ResultStatus> builder)
    {
        builder.ToTable("resultStatus", "core");
        builder.HasKey(x => x.StatusId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.IsLocked).HasDefaultValue(true);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.NameAr).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("ckResultStatus1", "\"statusId\" IN (1,2,3)"));
    }
}

public class RequiredActionTypeConfiguration : IEntityTypeConfiguration<RequiredActionType>
{
    public void Configure(EntityTypeBuilder<RequiredActionType> builder)
    {
        builder.ToTable("requiredActionType", "core");
        builder.HasKey(x => x.ActionTypeId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.RequiresDescription).HasDefaultValue(false);
        builder.Property(x => x.RequiresTargetClinic).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.NameAr).IsUnique();
    }
}

public class BmiCategoryConfiguration : IEntityTypeConfiguration<BmiCategory>
{
    public void Configure(EntityTypeBuilder<BmiCategory> builder)
    {
        builder.ToTable("bmiCategory", "core");
        builder.HasKey(x => x.CategoryId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.MinBmi).HasPrecision(4, 1);
        builder.Property(x => x.MaxBmi).HasPrecision(4, 1);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.NameAr).IsUnique();
        builder.HasIndex(x => x.SortOrder).IsUnique();
        builder.HasIndex(x => new { x.MinBmi, x.MaxBmi, x.IsActive });
        builder.ToTable(t => t.HasCheckConstraint("ckBmiCategory1", "\"minBmi\" >= 0 AND (\"maxBmi\" IS NULL OR \"maxBmi\" > \"minBmi\")"));
    }
}

public class MaritalStatusConfiguration : IEntityTypeConfiguration<MaritalStatus>
{
    public void Configure(EntityTypeBuilder<MaritalStatus> builder)
    {
        builder.ToTable("maritalStatus", "core");
        builder.HasKey(x => x.MaritalStatusId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.MaritalStatusId).UseIdentityByDefaultColumn();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.SortOrder).HasDefaultValue((short)0);
        builder.Property(x => x.IsSystem).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.NameAr).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
        builder.ToTable(t => t.HasCheckConstraint("ckMaritalStatus1", "\"sortOrder\" >= 0"));
    }
}

public class BloodTypeConfiguration : IEntityTypeConfiguration<BloodType>
{
    public void Configure(EntityTypeBuilder<BloodType> builder)
    {
        builder.ToTable("bloodType", "core");
        builder.HasKey(x => x.BloodTypeId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.BloodTypeId).UseIdentityByDefaultColumn();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.SortOrder).HasDefaultValue((short)0);
        builder.Property(x => x.IsSystem).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.NameAr).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
        builder.ToTable(t => t.HasCheckConstraint("ckBloodType1", "\"sortOrder\" >= 0"));
    }
}

public class ConsultationTypeConfiguration : IEntityTypeConfiguration<ConsultationType>
{
    public void Configure(EntityTypeBuilder<ConsultationType> builder)
    {
        builder.ToTable("consultationType", "core");
        builder.HasKey(x => x.ConsultationTypeId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.ConsultationTypeId).UseIdentityByDefaultColumn();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.SortOrder).HasDefaultValue((short)0);
        builder.Property(x => x.IsSystem).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.NameAr).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
        builder.ToTable(t => t.HasCheckConstraint("ckConsultationType1", "\"sortOrder\" >= 0"));
    }
}

public class InvestigationTypeConfiguration : IEntityTypeConfiguration<InvestigationType>
{
    public void Configure(EntityTypeBuilder<InvestigationType> builder)
    {
        builder.ToTable("investigationType", "core");
        builder.HasKey(x => x.InvestigationTypeId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.InvestigationTypeId).UseIdentityByDefaultColumn();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.SortOrder).HasDefaultValue((short)0);
        builder.Property(x => x.IsSystem).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.NameAr).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
        builder.ToTable(t => t.HasCheckConstraint("ckInvestigationType1", "\"sortOrder\" >= 0"));
    }
}

public class ValidationRuleConfiguration : IEntityTypeConfiguration<ValidationRule>
{
    public void Configure(EntityTypeBuilder<ValidationRule> builder)
    {
        builder.ToTable("validationRule", "core");
        builder.HasKey(x => x.ValidationRuleId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.Code).HasColumnType("citext");
        builder.Property(x => x.Unit).HasColumnType("citext");
        builder.Property(x => x.MinValue).HasPrecision(12, 4);
        builder.Property(x => x.MaxValue).HasPrecision(12, 4);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => new { x.Code, x.VersionNo }).IsUnique();
        builder.HasIndex(x => new { x.Code, x.IsActive, x.ValidFrom, x.ValidTo });
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckValidationRule1", "\"minValue\" IS NOT NULL OR \"maxValue\" IS NOT NULL");
            t.HasCheckConstraint("ckValidationRule2", "\"minValue\" IS NULL OR \"maxValue\" IS NULL OR \"maxValue\" >= \"minValue\"");
            t.HasCheckConstraint("ckValidationRule3", "\"validTo\" IS NULL OR \"validTo\" > \"validFrom\"");
            t.HasCheckConstraint("ckValidationRule4", "\"versionNo\" > 0");
        });
    }
}
