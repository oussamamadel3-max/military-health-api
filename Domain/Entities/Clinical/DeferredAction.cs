using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Clinical;

public class DeferredAction
{
    public Guid DeferredActionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid? ClinicId { get; set; }
    public Guid? ClinicAssessmentId { get; set; }
    public Guid? FinalDecisionId { get; set; }
    public string DeferReason { get; set; } = null!;
    public DateOnly? DeferUntil { get; set; }
    public short ActionTypeId { get; set; }
    public Guid? TargetClinicId { get; set; }
    public string? ActionDescription { get; set; }
    public string FollowupNote { get; set; } = null!;
    public string Status { get; set; } = "REQUESTED";
    public string RequestedByUserId { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; }
    public string? CompletedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public Clinic? Clinic { get; set; }
    public Clinic? TargetClinic { get; set; }
    public RequiredActionType ActionType { get; set; } = null!;
    public User RequestedByUser { get; set; } = null!;
    public User? CompletedByUser { get; set; }
}
