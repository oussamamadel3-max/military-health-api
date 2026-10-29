using Domain.Entities.Auth;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Medical;

public class Investigation
{
    public Guid InvestigationId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public short InvestigationTypeId { get; set; }
    public string TestName { get; set; } = null!;
    public string? RequestReason { get; set; }
    public string Status { get; set; } = "PENDING";
    public string? ResultText { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResultedAt { get; set; }

    /// <summary>
    /// ربط اختياري بإجراء مؤجّل عند الإنشاء من حالة مؤجلة (FR-047 / A3)؛ لا يُغلق الإجراء تلقائياً.
    /// </summary>
    public Guid? DeferredActionId { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public Clinic Clinic { get; set; } = null!;
    public InvestigationType InvestigationType { get; set; } = null!;
    public User RequestedByUser { get; set; } = null!;
    public DeferredAction? DeferredAction { get; set; }
}
