using Domain.Entities.Medical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Medical;

public class ConsultationConfiguration : IEntityTypeConfiguration<Consultation>
{
    public void Configure(EntityTypeBuilder<Consultation> builder)
    {
        builder.ToTable("consultation", "medical");
        builder.HasKey(x => x.ConsultationId);
        builder.UseCamelCaseColumns();
        builder.HasAlternateKey(x => new { x.ConsultationId, x.MemberId, x.CenterId });
        builder.Property(x => x.Status).HasColumnType("citext").HasDefaultValue("PENDING");
        builder.HasIndex(x => new { x.MemberId, x.RequestedAt });
        builder.HasIndex(x => x.ConsultationTypeId);
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ConsultationType).WithMany().HasForeignKey(x => x.ConsultationTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ConsultantUser).WithMany().HasForeignKey(x => x.ConsultantUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckConsultation1", "\"status\" IN ('PENDING','COMPLETED','CANCELLED')");
            t.HasCheckConstraint("ckConsultation2", "(\"status\" = 'COMPLETED' AND \"resultText\" IS NOT NULL AND btrim(\"resultText\") <> '' AND \"resultedAt\" IS NOT NULL) OR (\"status\" IN ('PENDING','CANCELLED') AND \"resultText\" IS NULL AND \"resultedAt\" IS NULL)");
            t.HasCheckConstraint("ckConsultation4", "\"resultedAt\" IS NULL OR \"resultedAt\" >= \"requestedAt\"");
        });
    }
}

public class InvestigationConfiguration : IEntityTypeConfiguration<Investigation>
{
    public void Configure(EntityTypeBuilder<Investigation> builder)
    {
        builder.ToTable("investigation", "medical");
        builder.HasKey(x => x.InvestigationId);
        builder.UseCamelCaseColumns();
        builder.HasAlternateKey(x => new { x.InvestigationId, x.MemberId, x.CenterId });
        builder.Property(x => x.Status).HasColumnType("citext").HasDefaultValue("PENDING");
        builder.HasIndex(x => new { x.MemberId, x.RequestedAt });
        builder.HasIndex(x => new { x.CenterId, x.Status });
        builder.HasIndex(x => x.InvestigationTypeId);
        builder.HasIndex(x => x.DeferredActionId);
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InvestigationType).WithMany().HasForeignKey(x => x.InvestigationTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DeferredAction).WithMany().HasForeignKey(x => x.DeferredActionId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckInvestigation1", "\"status\" IN ('PENDING','COMPLETED','CANCELLED')");
            t.HasCheckConstraint("ckInvestigation2", "(\"status\" = 'COMPLETED' AND \"resultText\" IS NOT NULL AND btrim(\"resultText\") <> '' AND \"resultedAt\" IS NOT NULL) OR (\"status\" IN ('PENDING','CANCELLED') AND \"resultText\" IS NULL AND \"resultedAt\" IS NULL)");
            t.HasCheckConstraint("ckInvestigation3", "\"resultedAt\" IS NULL OR \"resultedAt\" >= \"requestedAt\"");
        });
    }
}

public class MedicalAttachmentConfiguration : IEntityTypeConfiguration<MedicalAttachment>
{
    public void Configure(EntityTypeBuilder<MedicalAttachment> builder)
    {
        builder.ToTable("medicalAttachment", "medical");
        builder.HasKey(x => x.AttachmentId);
        builder.UseCamelCaseColumns();
        builder.HasIndex(x => x.FileAssetId).IsUnique();
        builder.HasIndex(x => new { x.MemberId, x.CreatedAt });
        builder.HasIndex(x => new { x.ConsultationId, x.MemberId, x.CenterId });
        builder.HasIndex(x => new { x.InvestigationId, x.MemberId, x.CenterId });
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FileAsset).WithMany().HasForeignKey(x => x.FileAssetId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.FileAsset.IsDeleted);
        builder.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Consultation>().WithMany().HasForeignKey(x => new { x.ConsultationId, x.MemberId, x.CenterId })
            .HasPrincipalKey(x => new { x.ConsultationId, x.MemberId, x.CenterId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Investigation>().WithMany().HasForeignKey(x => new { x.InvestigationId, x.MemberId, x.CenterId })
            .HasPrincipalKey(x => new { x.InvestigationId, x.MemberId, x.CenterId }).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckMedicalAttachment1", "(\"consultationId\" IS NOT NULL) <> (\"investigationId\" IS NOT NULL)"));
    }
}
