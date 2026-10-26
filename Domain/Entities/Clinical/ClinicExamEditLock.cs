using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Clinical;

/// <summary>
/// قفل تحرير مؤقت لمنتسب واحد داخل عيادة واحدة (شريحة فتح FR-045).
/// مدة الانتهاء مؤقتة حتى تعتمد الجهة مهلة رسمية.
/// </summary>
public class ClinicExamEditLock
{
    public Guid LockId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string LockedByUserId { get; set; } = null!;
    public DateTimeOffset LockedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public Clinic Clinic { get; set; } = null!;
    public User LockedByUser { get; set; } = null!;
}
