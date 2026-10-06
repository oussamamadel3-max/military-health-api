using Domain.Entities.Clinical;
using Domain.Entities.Export;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Export;

public class RecruitmentExportBatchConfiguration : IEntityTypeConfiguration<RecruitmentExportBatch>
{
    public void Configure(EntityTypeBuilder<RecruitmentExportBatch> builder)
    {
        builder.ToTable("recruitmentExportBatch", "export");
        builder.HasKey(x => x.ExportBatchId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.CenterCodeSnapshot).HasColumnType("citext");
        builder.Property(x => x.RecordCount).HasDefaultValue(0);
        builder.Property(x => x.Status).HasColumnType("citext").HasDefaultValue("GENERATING");
        builder.Property(x => x.FilterSnapshot).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.CenterId, x.CreatedAt });
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ExportedByUser).WithMany().HasForeignKey(x => x.ExportedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.GeneratedFile).WithMany().HasForeignKey(x => x.GeneratedFileId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckRecruitmentExportBatch1", "\"recordCount\" >= 0");
            t.HasCheckConstraint("ckRecruitmentExportBatch2", "\"status\" IN ('GENERATING','COMPLETED','FAILED')");
            t.HasCheckConstraint("ckRecruitmentExportBatch3", "(\"status\" = 'COMPLETED' AND \"generatedFileId\" IS NOT NULL AND \"completedAt\" IS NOT NULL) OR (\"status\" = 'GENERATING' AND \"completedAt\" IS NULL) OR (\"status\" = 'FAILED' AND \"completedAt\" IS NOT NULL)");
        });
    }
}

public class RecruitmentExportItemConfiguration : IEntityTypeConfiguration<RecruitmentExportItem>
{
    public void Configure(EntityTypeBuilder<RecruitmentExportItem> builder)
    {
        builder.ToTable("recruitmentExportItem", "export");
        builder.HasKey(x => new { x.ExportBatchId, x.MemberId });
        builder.UseCamelCaseColumns();
        builder.Property(x => x.RecruitmentNumberSnapshot).HasColumnType("citext");
        builder.Property(x => x.RecruitmentCenterCodeSnapshot).HasColumnType("citext");
        builder.Property(x => x.IsReexport).HasDefaultValue(false);
        builder.HasIndex(x => new { x.MemberId, x.ExportedAt });
        builder.HasIndex(x => x.RecruitmentCenterId);
        builder.HasOne(x => x.ExportBatch).WithMany().HasForeignKey(x => x.ExportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ResultStatus).WithMany().HasForeignKey(x => x.ResultStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RecruitmentCenter).WithMany().HasForeignKey(x => x.RecruitmentCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinalDecision>().WithMany().HasForeignKey(x => new { x.FinalDecisionId, x.MemberId })
            .HasPrincipalKey(x => new { x.FinalDecisionId, x.MemberId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RecruitmentExportItem>().WithMany().HasForeignKey(x => new { x.PreviousExportBatchId, x.MemberId })
            .HasPrincipalKey(x => new { x.ExportBatchId, x.MemberId }).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ckRecruitmentExportItem1", "(\"resultStatusId\" = 1 OR (\"resultStatusId\" IN (2,3) AND \"resultReasonSnapshot\" IS NOT NULL AND btrim(\"resultReasonSnapshot\") <> ''))");
            t.HasCheckConstraint("ckRecruitmentExportItem2", "((\"isReexport\" = false AND \"previousExportBatchId\" IS NULL) OR (\"isReexport\" = true AND \"previousExportBatchId\" IS NOT NULL))");
        });
    }
}
