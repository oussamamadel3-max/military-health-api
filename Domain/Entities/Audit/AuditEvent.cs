using System.Net;
using Domain.Entities.Auth;
using Domain.Entities.Core;

namespace Domain.Entities.Audit;

public class AuditEvent
{
    public long EventId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? ActorUserId { get; set; }
    public string ContextScope { get; set; } = "CENTRAL";
    public Guid? CenterId { get; set; }
    public string EventType { get; set; } = null!;
    public string Result { get; set; } = "SUCCESS";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? ReasonText { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public IPAddress? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public Guid CorrelationId { get; set; }

    public User? ActorUser { get; set; }
    public MedicalCenter? Center { get; set; }
}
