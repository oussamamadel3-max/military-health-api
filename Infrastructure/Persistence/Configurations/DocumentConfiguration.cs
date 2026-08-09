using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.DocumentId);

        builder.Property(d => d.UserId)
            .IsRequired();

        builder.Property(d => d.DocumentNumber)
            .IsRequired();

        builder.Property(d => d.Title)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(d => d.Description)
            .HasMaxLength(2000);

        builder.HasIndex(d => d.DocumentNumber)
            .IsUnique();

        builder.HasIndex(d => d.UserId);

        builder.HasOne(d => d.User)
            .WithMany(u => u.Documents)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
