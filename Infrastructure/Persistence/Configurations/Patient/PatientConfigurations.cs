using Domain.Entities.Patient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Patient;

public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("member", "patient");
        builder.HasKey(x => x.MemberId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.RecruitmentNumber).HasColumnType("citext");
        builder.Property(x => x.BloodSugarMgDl).HasPrecision(6, 2);
        builder.Property(x => x.HeightCm).HasPrecision(5, 2);
        builder.Property(x => x.WeightKg).HasPrecision(6, 2);
        builder.Property(x => x.Bmi).HasPrecision(4, 1).ValueGeneratedOnAddOrUpdate()
            .HasComputedColumnSql("""CASE WHEN "heightCm" IS NULL OR "weightKg" IS NULL THEN NULL ELSE round(("weightKg" / power(("heightCm" / 100.0), 2))::numeric, 1) END""", stored: true);
        builder.Property(x => x.BloodPressure).HasMaxLength(15);
        builder.Property(x => x.HasTattoo).HasDefaultValue(false);
        builder.Property(x => x.ExportStatus).HasColumnType("citext").HasDefaultValue("UNEXPORTED");
        builder.Property(x => x.DataRevision).HasDefaultValue(1L);
        builder.HasIndex(x => x.FileNumber).IsUnique();
        builder.HasIndex(x => x.RecruitmentNumber).IsUnique();
        builder.HasIndex(x => new { x.QueueMedicalCenterId, x.QueueDate, x.QueueNumber }).IsUnique();
        builder.HasIndex(x => new { x.CurrentMedicalCenterId, x.CreatedAt });
        builder.HasIndex(x => x.FullName);
        builder.HasIndex(x => new { x.CurrentMedicalCenterId, x.ExportStatus });
        builder.HasIndex(x => x.RecruitmentCenterId);
        builder.HasIndex(x => new { x.CurrentMedicalCenterId, x.ReviewedRevision, x.DataRevision });
        builder.HasIndex(x => x.MaritalStatusId);
        builder.HasIndex(x => x.BloodTypeId);
        builder.HasOne(x => x.MaritalStatus).WithMany().HasForeignKey(x => x.MaritalStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RecruitmentCenter).WithMany().HasForeignKey(x => x.RecruitmentCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BloodType).WithMany().HasForeignKey(x => x.BloodTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CurrentMedicalCenter).WithMany().HasForeignKey(x => x.CurrentMedicalCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.QueueMedicalCenter).WithMany().HasForeignKey(x => x.QueueMedicalCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReviewedByUser).WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckMember1", "\"bloodSugarMgDl\" IS NULL OR \"bloodSugarMgDl\" > 0");
            t.HasCheckConstraint("ckMember2", "\"heightCm\" IS NULL OR \"heightCm\" > 0");
            t.HasCheckConstraint("ckMember3", "\"weightKg\" IS NULL OR \"weightKg\" > 0");
            t.HasCheckConstraint("ckMember4", "\"pulseBpm\" IS NULL OR \"pulseBpm\" > 0");
            t.HasCheckConstraint("ckMember5", "\"queueNumber\" > 0");
            t.HasCheckConstraint("ckMember6", "\"exportStatus\" IN ('UNEXPORTED','EXPORTED')");
            t.HasCheckConstraint("ckMember7", "((\"reviewedByUserId\" IS NULL AND \"reviewedAt\" IS NULL AND \"reviewedRevision\" IS NULL) OR (\"reviewedByUserId\" IS NOT NULL AND \"reviewedAt\" IS NOT NULL AND \"reviewedRevision\" IS NOT NULL))");
            t.HasCheckConstraint("ckMember8", "\"reviewedRevision\" IS NULL OR \"reviewedRevision\" <= \"dataRevision\"");
        });
    }
}

public class MemberCenterIdentityConfiguration : IEntityTypeConfiguration<MemberCenterIdentity>
{
    public void Configure(EntityTypeBuilder<MemberCenterIdentity> builder)
    {
        builder.ToTable("memberCenterIdentity", "patient");
        builder.HasKey(x => x.MemberCenterIdentityId);
        builder.UseCamelCaseColumns();
        builder.HasAlternateKey(x => new { x.MemberCenterIdentityId, x.MemberId, x.MedicalCenterId });
        builder.Property(x => x.CenterMemberId).HasColumnType("citext");
        builder.HasIndex(x => new { x.MemberId, x.ValidFrom });
        builder.HasIndex(x => new { x.MedicalCenterId, x.CenterMemberId });
        builder.HasIndex(x => x.MemberId).IsUnique().HasFilter("\"validTo\" IS NULL");
        builder.HasIndex(x => new { x.MedicalCenterId, x.CenterMemberId }).IsUnique().HasFilter("\"validTo\" IS NULL");
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MedicalCenter).WithMany().HasForeignKey(x => x.MedicalCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AssignedByUser).WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ClosedByUser).WithMany().HasForeignKey(x => x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckMemberCenterIdentity2", "\"validTo\" IS NULL OR \"validTo\" >= \"validFrom\"");
            t.HasCheckConstraint("ckMemberCenterIdentity3", "((\"validTo\" IS NULL AND \"closedByUserId\" IS NULL) OR (\"validTo\" IS NOT NULL AND \"closedByUserId\" IS NOT NULL))");
        });
    }
}

public class MemberTransferConfiguration : IEntityTypeConfiguration<MemberTransfer>
{
    public void Configure(EntityTypeBuilder<MemberTransfer> builder)
    {
        builder.ToTable("memberTransfer", "patient");
        builder.HasKey(x => x.TransferId);
        builder.UseCamelCaseColumns();
        builder.HasIndex(x => new { x.MemberId, x.ExecutedAt });
        builder.HasIndex(x => new { x.FromMemberCenterIdentityId, x.ToMemberCenterIdentityId });
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FromCenter).WithMany().HasForeignKey(x => x.FromCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToCenter).WithMany().HasForeignKey(x => x.ToCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ExecutedByUser).WithMany().HasForeignKey(x => x.ExecutedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MemberCenterIdentity>().WithMany().HasForeignKey(x => new { x.FromMemberCenterIdentityId, x.MemberId, x.FromCenterId })
            .HasPrincipalKey(x => new { x.MemberCenterIdentityId, x.MemberId, x.MedicalCenterId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MemberCenterIdentity>().WithMany().HasForeignKey(x => new { x.ToMemberCenterIdentityId, x.MemberId, x.ToCenterId })
            .HasPrincipalKey(x => new { x.MemberCenterIdentityId, x.MemberId, x.MedicalCenterId }).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckMemberTransfer1", "\"fromCenterId\" <> \"toCenterId\"");
            t.HasCheckConstraint("ckMemberTransfer2", "\"fromMemberCenterIdentityId\" <> \"toMemberCenterIdentityId\"");
        });
    }
}

public class MemberGeneralNoteConfiguration : IEntityTypeConfiguration<MemberGeneralNote>
{
    public void Configure(EntityTypeBuilder<MemberGeneralNote> builder)
    {
        builder.ToTable("memberGeneralNote", "patient");
        builder.HasKey(x => x.NoteId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => x.MemberId).IsUnique();
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AuthorClinic).WithMany().HasForeignKey(x => x.AuthorClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AuthorUser).WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
