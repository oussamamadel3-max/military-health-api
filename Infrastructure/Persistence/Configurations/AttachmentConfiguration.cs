using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.HasKey(a => a.AttachmentId);

        builder.Property(a => a.OwnerType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.OwnerId)
            .IsRequired();

        builder.Property(a => a.FileName)
            .IsRequired()
            .HasMaxLength(260);

        builder.Property(a => a.FileExtension)
            .HasMaxLength(20);

        builder.Property(a => a.FilePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(a => a.ContentType)
            .HasMaxLength(150);

        builder.Property(a => a.Description)
            .HasMaxLength(1000);

        builder.Property(a => a.FileSize)
            .IsRequired();

        builder.Property(a => a.IsProfilePhoto)
            .HasDefaultValue(false);

        builder.Property(a => a.UploadedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(a => new { a.OwnerType, a.OwnerId });

        builder.HasIndex(a => new { a.OwnerType, a.OwnerId, a.IsProfilePhoto })
            .HasFilter("\"IsProfilePhoto\" = true")
            .IsUnique();
    }
}
