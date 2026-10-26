using Domain.Entities.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Clinical;

public class MedicalExamConfiguration : IEntityTypeConfiguration<MedicalExam>
{
    public void Configure(EntityTypeBuilder<MedicalExam> builder)
    {
        builder.ToTable("medicalExam", "clinical");
        builder.HasKey(x => x.ExamId);
        builder.UseCamelCaseColumns();
        builder.HasAlternateKey(x => new { x.ExamId, x.MemberId, x.CenterId, x.ClinicId });
        builder.Property(x => x.ExamSequence).HasDefaultValue((short)1);
        builder.Property(x => x.IsCurrent).HasDefaultValue(true);
        builder.Property(x => x.VersionNo).HasDefaultValue(1);
        builder.HasIndex(x => new { x.MemberId, x.ClinicId, x.ExamSequence }).IsUnique();
        builder.HasIndex(x => new { x.MemberId, x.ClinicId }).IsUnique().HasFilter("\"isCurrent\" = true");
        builder.HasIndex(x => new { x.CenterId, x.ClinicId, x.ExaminedAt });
        builder.HasIndex(x => x.MemberId);
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ExaminerUser).WithMany().HasForeignKey(x => x.ExaminerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckMedicalExam1", "\"examSequence\" > 0");
            t.HasCheckConstraint("ckMedicalExam2", "\"versionNo\" > 0");
        });
    }
}

public class ClinicAssessmentConfiguration : IEntityTypeConfiguration<ClinicAssessment>
{
    public void Configure(EntityTypeBuilder<ClinicAssessment> builder)
    {
        builder.ToTable("clinicAssessment", "clinical");
        builder.HasKey(x => x.AssessmentId);
        builder.UseCamelCaseColumns();
        builder.HasAlternateKey(x => new { x.AssessmentId, x.MemberId, x.CenterId, x.ClinicId });
        builder.HasAlternateKey(x => new { x.AssessmentId, x.MemberId, x.CenterId });
        builder.HasIndex(x => x.ExamId).IsUnique();
        builder.HasIndex(x => new { x.MemberId, x.ClinicId });
        builder.HasIndex(x => new { x.CenterId, x.ResultStatusId });
        builder.HasOne(x => x.Exam).WithMany().HasForeignKey(x => new { x.ExamId, x.MemberId, x.CenterId, x.ClinicId })
            .HasPrincipalKey(x => new { x.ExamId, x.MemberId, x.CenterId, x.ClinicId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ResultStatus).WithMany().HasForeignKey(x => x.ResultStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AssessedByUser).WithMany().HasForeignKey(x => x.AssessedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckClinicAssessment1",
            "(\"resultStatusId\" = 2 AND \"rejectionReason\" IS NOT NULL AND btrim(\"rejectionReason\") <> '') OR (\"resultStatusId\" <> 2 AND \"rejectionReason\" IS NULL)"));
    }
}

public class EyeExamDetailConfiguration : IEntityTypeConfiguration<EyeExamDetail>
{
    public void Configure(EntityTypeBuilder<EyeExamDetail> builder)
    {
        builder.ToTable("eyeExamDetail", "clinical");
        builder.HasKey(x => x.ExamId);
        builder.UseCamelCaseColumns();
        builder.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EyeRefractionDetailConfiguration : IEntityTypeConfiguration<EyeRefractionDetail>
{
    public void Configure(EntityTypeBuilder<EyeRefractionDetail> builder)
    {
        builder.ToTable("eyeRefractionDetail", "clinical");
        builder.HasKey(x => x.RefractionDetailId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.EyeSide).HasColumnType("citext");
        builder.Property(x => x.RefractionType).HasColumnType("citext");
        builder.Property(x => x.InputFormat).HasColumnType("citext");
        builder.Property(x => x.Numerator).HasPrecision(10, 4);
        builder.Property(x => x.Denominator).HasPrecision(10, 4);
        builder.Property(x => x.DecimalInput).HasPrecision(10, 4);
        builder.Property(x => x.CalculatedValue).HasPrecision(10, 4).ValueGeneratedOnAddOrUpdate()
            .HasComputedColumnSql("""CASE WHEN "inputFormat" = 'FRACTION' THEN ("numerator" / NULLIF("denominator", 0)) ELSE "decimalInput" END""", stored: true);
        builder.Property(x => x.IsWorst).HasDefaultValue(false);
        builder.HasIndex(x => new { x.ExamId, x.EyeSide });
        builder.HasIndex(x => new { x.ExamId, x.EyeSide, x.RefractionType }).IsUnique().HasFilter("\"isWorst\" = true");
        builder.HasOne(x => x.EyeExamDetail).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckEyeRefractionDetail1", "\"eyeSide\" IN ('RIGHT','LEFT')");
            t.HasCheckConstraint("ckEyeRefractionDetail2", "\"inputFormat\" IN ('FRACTION','DECIMAL')");
            t.HasCheckConstraint("ckEyeRefractionDetail3", "(\"inputFormat\" = 'FRACTION' AND \"numerator\" IS NOT NULL AND \"denominator\" IS NOT NULL AND \"denominator\" > 0 AND \"decimalInput\" IS NULL) OR (\"inputFormat\" = 'DECIMAL' AND \"decimalInput\" IS NOT NULL AND \"numerator\" IS NULL AND \"denominator\" IS NULL)");
        });
    }
}

public class InternalExamDetailConfiguration : IEntityTypeConfiguration<InternalExamDetail>
{
    public void Configure(EntityTypeBuilder<InternalExamDetail> builder)
    {
        builder.ToTable("internalExamDetail", "clinical");
        builder.HasKey(x => x.ExamId);
        builder.UseCamelCaseColumns();
        builder.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SurgeryExamDetailConfiguration : IEntityTypeConfiguration<SurgeryExamDetail>
{
    public void Configure(EntityTypeBuilder<SurgeryExamDetail> builder)
    {
        builder.ToTable("surgeryExamDetail", "clinical");
        builder.HasKey(x => x.ExamId);
        builder.UseCamelCaseColumns();
        builder.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OrthopedicExamDetailConfiguration : IEntityTypeConfiguration<OrthopedicExamDetail>
{
    public void Configure(EntityTypeBuilder<OrthopedicExamDetail> builder)
    {
        builder.ToTable("orthopedicExamDetail", "clinical");
        builder.HasKey(x => x.ExamId);
        builder.UseCamelCaseColumns();
        builder.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EntExamDetailConfiguration : IEntityTypeConfiguration<EntExamDetail>
{
    public void Configure(EntityTypeBuilder<EntExamDetail> builder)
    {
        builder.ToTable("entExamDetail", "clinical");
        builder.HasKey(x => x.ExamId);
        builder.UseCamelCaseColumns();
        builder.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class DeferredActionConfiguration : IEntityTypeConfiguration<DeferredAction>
{
    public void Configure(EntityTypeBuilder<DeferredAction> builder)
    {
        builder.ToTable("deferredAction", "clinical");
        builder.HasKey(x => x.DeferredActionId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.Status).HasColumnType("citext").HasDefaultValue("REQUESTED");
        builder.HasIndex(x => new { x.CenterId, x.Status, x.DeferUntil });
        builder.HasIndex(x => x.MemberId);
        builder.HasIndex(x => x.ClinicAssessmentId).IsUnique().HasFilter("\"clinicAssessmentId\" IS NOT NULL AND \"status\" <> 'CLOSED'");
        builder.HasIndex(x => x.FinalDecisionId).IsUnique().HasFilter("\"finalDecisionId\" IS NOT NULL AND \"status\" <> 'CLOSED'");
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TargetClinic).WithMany().HasForeignKey(x => x.TargetClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ActionType).WithMany().HasForeignKey(x => x.ActionTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CompletedByUser).WithMany().HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClinicAssessment>().WithMany().HasForeignKey(x => new { x.ClinicAssessmentId, x.MemberId, x.CenterId, x.ClinicId })
            .HasPrincipalKey(x => new { x.AssessmentId, x.MemberId, x.CenterId, x.ClinicId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinalDecision>().WithMany().HasForeignKey(x => new { x.FinalDecisionId, x.MemberId, x.CenterId })
            .HasPrincipalKey(x => new { x.FinalDecisionId, x.MemberId, x.CenterId }).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckDeferredAction1", "(\"clinicAssessmentId\" IS NOT NULL) <> (\"finalDecisionId\" IS NOT NULL)");
            t.HasCheckConstraint("ckDeferredAction2", "\"status\" IN ('REQUESTED','COMPLETED','CLOSED')");
            t.HasCheckConstraint("ckDeferredAction3", "((\"clinicAssessmentId\" IS NOT NULL AND \"finalDecisionId\" IS NULL AND \"clinicId\" IS NOT NULL) OR (\"finalDecisionId\" IS NOT NULL AND \"clinicAssessmentId\" IS NULL AND \"clinicId\" IS NULL))");
            t.HasCheckConstraint("ckDeferredAction4", "btrim(\"deferReason\") <> ''");
            t.HasCheckConstraint("ckDeferredAction6", "\"finalDecisionId\" IS NULL OR \"deferUntil\" IS NOT NULL");
        });
    }
}

public class FinalDecisionConfiguration : IEntityTypeConfiguration<FinalDecision>
{
    public void Configure(EntityTypeBuilder<FinalDecision> builder)
    {
        builder.ToTable("finalDecision", "clinical");
        builder.HasKey(x => x.FinalDecisionId);
        builder.UseCamelCaseColumns();
        builder.HasAlternateKey(x => new { x.FinalDecisionId, x.MemberId, x.CenterId });
        builder.HasAlternateKey(x => new { x.FinalDecisionId, x.MemberId });
        builder.HasIndex(x => x.MemberId).IsUnique();
        builder.HasIndex(x => new { x.CenterId, x.ResultStatusId, x.DecidedAt });
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SupervisorUser).WithMany().HasForeignKey(x => x.SupervisorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ResultStatus).WithMany().HasForeignKey(x => x.ResultStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckFinalDecision1",
            "(\"resultStatusId\" = 2 AND \"rejectionReason\" IS NOT NULL AND btrim(\"rejectionReason\") <> '') OR (\"resultStatusId\" <> 2 AND \"rejectionReason\" IS NULL)"));
    }
}

public class FinalDecisionHistoryConfiguration : IEntityTypeConfiguration<FinalDecisionHistory>
{
    public void Configure(EntityTypeBuilder<FinalDecisionHistory> builder)
    {
        builder.ToTable("finalDecisionHistory", "clinical");
        builder.HasKey(x => x.HistoryId);
        builder.UseCamelCaseColumns();
        builder.HasIndex(x => new { x.MemberId, x.ChangedAt });
        builder.HasOne(x => x.FinalDecision).WithMany().HasForeignKey(x => new { x.FinalDecisionId, x.MemberId, x.CenterId })
            .HasPrincipalKey(x => new { x.FinalDecisionId, x.MemberId, x.CenterId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OldStatus).WithMany().HasForeignKey(x => x.OldStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.NewStatus).WithMany().HasForeignKey(x => x.NewStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ChangedByUser).WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckFinalDecisionHistory1", "\"oldStatusId\" <> \"newStatusId\""));
    }
}

public class SupervisorObjectionConfiguration : IEntityTypeConfiguration<SupervisorObjection>
{
    public void Configure(EntityTypeBuilder<SupervisorObjection> builder)
    {
        builder.ToTable("supervisorObjection", "clinical");
        builder.HasKey(x => x.ObjectionId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.TargetScope).HasColumnType("citext");
        builder.Property(x => x.Status).HasColumnType("citext").HasDefaultValue("OPEN");
        builder.HasIndex(x => new { x.MemberId, x.Status });
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SupervisorUser).WithMany().HasForeignKey(x => x.SupervisorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClinicAssessment>().WithMany().HasForeignKey(x => new { x.ClinicAssessmentId, x.MemberId, x.CenterId })
            .HasPrincipalKey(x => new { x.AssessmentId, x.MemberId, x.CenterId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinalDecision>().WithMany().HasForeignKey(x => new { x.FinalDecisionId, x.MemberId, x.CenterId })
            .HasPrincipalKey(x => new { x.FinalDecisionId, x.MemberId, x.CenterId }).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckSupervisorObjection1", "\"targetScope\" IN ('ONE_CLINIC','ALL_CLINICS')");
            t.HasCheckConstraint("ckSupervisorObjection2", "(\"targetScope\" = 'ONE_CLINIC' AND \"clinicAssessmentId\" IS NOT NULL) OR (\"targetScope\" = 'ALL_CLINICS' AND \"clinicAssessmentId\" IS NULL)");
            t.HasCheckConstraint("ckSupervisorObjection3", "\"status\" IN ('OPEN','RESOLVED','DISMISSED')");
            t.HasCheckConstraint("ckSupervisorObjection4", "(\"status\" = 'OPEN' AND \"resolvedAt\" IS NULL AND \"resolutionNote\" IS NULL) OR (\"status\" IN ('RESOLVED','DISMISSED') AND \"resolvedAt\" IS NOT NULL AND \"resolutionNote\" IS NOT NULL AND btrim(\"resolutionNote\") <> '')");
            t.HasCheckConstraint("ckSupervisorObjection5", "\"resolvedAt\" IS NULL OR \"resolvedAt\" >= \"createdAt\"");
        });
    }
}

/// <summary>تعيين EF لأقفال تحرير فحص العيادة المؤقتة (شريحة فتح FR-045).</summary>
public class ClinicExamEditLockConfiguration : IEntityTypeConfiguration<ClinicExamEditLock>
{
    public void Configure(EntityTypeBuilder<ClinicExamEditLock> builder)
    {
        builder.ToTable("clinicExamEditLock", "clinical");
        builder.HasKey(x => x.LockId);
        builder.UseCamelCaseColumns();
        builder.HasIndex(x => new { x.MemberId, x.ClinicId }).IsUnique();
        builder.HasIndex(x => new { x.CenterId, x.ClinicId, x.ExpiresAt });
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LockedByUser).WithMany().HasForeignKey(x => x.LockedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckClinicExamEditLock1", "\"expiresAt\" > \"lockedAt\""));
    }
}
