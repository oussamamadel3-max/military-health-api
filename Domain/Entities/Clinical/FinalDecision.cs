using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Clinical;

public class FinalDecision
{
    public Guid FinalDecisionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public string SupervisorUserId { get; set; } = null!;
    public short ResultStatusId { get; set; }
    public string? RejectionReason { get; set; }
    public string? DisagreementJustification { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public string UpdatedByUserId { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public ResultStatus ResultStatus { get; set; } = null!;
    public User SupervisorUser { get; set; } = null!;
    public User UpdatedByUser { get; set; } = null!;
}
