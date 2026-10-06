using Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Audit;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("event", "audit");
        builder.HasKey(x => x.EventId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.EventId).UseIdentityAlwaysColumn();
        builder.Property(x => x.ContextScope).HasColumnType("citext");
        builder.Property(x => x.EventType).HasColumnType("citext");
        builder.Property(x => x.Result).HasColumnType("citext");
        builder.Property(x => x.OldValues).HasColumnType("jsonb");
        builder.Property(x => x.NewValues).HasColumnType("jsonb");
        builder.Property(x => x.IPAddress).HasColumnType("inet").HasColumnName("ipAddress");
        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => new { x.ActorUserId, x.OccurredAt });
        builder.HasIndex(x => new { x.CenterId, x.OccurredAt });
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        builder.HasIndex(x => new { x.ContextScope, x.OccurredAt });
        builder.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Center).WithMany().HasForeignKey(x => x.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckAuditEventContext",
            "(\"contextScope\" = 'CENTER' AND \"centerId\" IS NOT NULL) OR (\"contextScope\" = 'CENTRAL' AND \"centerId\" IS NULL)"));
    }
}

public class CrossCenterAccessConfiguration : IEntityTypeConfiguration<CrossCenterAccess>
{
    public void Configure(EntityTypeBuilder<CrossCenterAccess> builder)
    {
        builder.ToTable("crossCenterAccess", "audit");
        builder.HasKey(x => x.AccessId);
        builder.UseCamelCaseColumns();
        builder.Property(x => x.QueryCriteria).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.AuditEventId, x.TargetCenterId }).IsUnique();
        builder.HasIndex(x => x.TargetCenterId);
        builder.HasOne(x => x.AuditEvent).WithMany().HasForeignKey(x => x.AuditEventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TargetCenter).WithMany().HasForeignKey(x => x.TargetCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("ckCrossCenterAccess1", "btrim(\"accessReason\") <> ''"));
    }
}
