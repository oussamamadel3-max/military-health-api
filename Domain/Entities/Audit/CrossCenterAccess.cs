using Domain.Entities.Core;

namespace Domain.Entities.Audit;

public class CrossCenterAccess
{
    public Guid AccessId { get; set; }
    public long AuditEventId { get; set; }
    public Guid TargetCenterId { get; set; }
    public string AccessReason { get; set; } = null!;
    public string DataType { get; set; } = null!;
    public string? QueryCriteria { get; set; }

    public AuditEvent AuditEvent { get; set; } = null!;
    public MedicalCenter TargetCenter { get; set; } = null!;
}
