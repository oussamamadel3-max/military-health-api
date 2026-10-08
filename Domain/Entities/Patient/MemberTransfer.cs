using Domain.Entities.Auth;
using Domain.Entities.Core;

namespace Domain.Entities.Patient;

public class MemberTransfer
{
    public Guid TransferId { get; set; }
    public Guid MemberId { get; set; }
    public Guid FromCenterId { get; set; }
    public Guid ToCenterId { get; set; }
    public Guid FromMemberCenterIdentityId { get; set; }
    public Guid ToMemberCenterIdentityId { get; set; }
    public string TransferReason { get; set; } = null!;
    public string ExecutedByUserId { get; set; } = null!;
    public DateTimeOffset ExecutedAt { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter FromCenter { get; set; } = null!;
    public MedicalCenter ToCenter { get; set; } = null!;
    public User ExecutedByUser { get; set; } = null!;
}
