using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Clinical;

public class FinalDecisionHistory
{
    public Guid HistoryId { get; set; }
    public Guid FinalDecisionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public short OldStatusId { get; set; }
    public short NewStatusId { get; set; }
    public string ChangeReason { get; set; } = null!;
    public string ChangedByUserId { get; set; } = null!;
    public DateTimeOffset ChangedAt { get; set; }
    public bool ExportResetPerformed { get; set; }

    public FinalDecision FinalDecision { get; set; } = null!;
    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public ResultStatus OldStatus { get; set; } = null!;
    public ResultStatus NewStatus { get; set; } = null!;
    public User ChangedByUser { get; set; } = null!;
}
