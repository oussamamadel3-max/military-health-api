using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Core;

public class FileAssetConfiguration : IEntityTypeConfiguration<FileAsset>
{
    public void Configure(EntityTypeBuilder<FileAsset> builder)
    {
        builder.ToTable("fileAsset", "core");
        builder.HasKey(x => x.FileAssetId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.StorageKey).IsRequired();
        builder.Property(x => x.OriginalName).IsRequired();
        builder.Property(x => x.MimeType).IsRequired();
        builder.Property(x => x.Extension).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.CenterId).IsRequired(false);
        builder.Property(x => x.UploadedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.HasIndex(x => new { x.CenterId, x.CreatedAt });
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.ToTable(t => t.HasCheckConstraint("ckFileAsset1", "\"sizeBytes\" > 0"));
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
