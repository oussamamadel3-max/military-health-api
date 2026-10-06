using Domain.Entities.Auth;
using Domain.Entities.Core;

namespace Domain.Entities.Patient;

public class MemberCenterIdentity
{
    public Guid MemberCenterIdentityId { get; set; }
    public Guid MemberId { get; set; }
    public Guid MedicalCenterId { get; set; }
    public string CenterMemberId { get; set; } = null!;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public string AssignedByUserId { get; set; } = null!;
    public string? ClosedByUserId { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter MedicalCenter { get; set; } = null!;
    public User AssignedByUser { get; set; } = null!;
    public User? ClosedByUser { get; set; }
}
