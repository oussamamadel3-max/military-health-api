using Domain.Entities.Archive;
using Domain.Entities.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Archive;

public class MemberArchiveConfiguration : IEntityTypeConfiguration<MemberArchive>
{
    public void Configure(EntityTypeBuilder<MemberArchive> builder)
    {
        builder.ToTable("memberArchive", "archive");
        builder.HasKey(x => x.ArchiveId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.ArchiveNumber).HasColumnType("citext");
        builder.HasIndex(x => x.MemberId).IsUnique();
        builder.HasIndex(x => x.FinalDecisionId).IsUnique();
        builder.HasIndex(x => x.ArchiveNumber).IsUnique();
        builder.HasIndex(x => new { x.CenterId, x.ArchivedAt });
        builder.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DigitalCopyFile).WithMany().HasForeignKey(x => x.DigitalCopyFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinalDecision>().WithMany().HasForeignKey(x => new { x.FinalDecisionId, x.MemberId, x.CenterId })
            .HasPrincipalKey(x => new { x.FinalDecisionId, x.MemberId, x.CenterId }).OnDelete(DeleteBehavior.Restrict);
    }
}
