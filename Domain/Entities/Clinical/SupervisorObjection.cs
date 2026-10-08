using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Clinical;

public class SupervisorObjection
{
    public Guid ObjectionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public string SupervisorUserId { get; set; } = null!;
    public string TargetScope { get; set; } = null!;
    public Guid? ClinicAssessmentId { get; set; }
    public string ObjectionReason { get; set; } = null!;
    public Guid? FinalDecisionId { get; set; }
    public string Status { get; set; } = "OPEN";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public User SupervisorUser { get; set; } = null!;
}
