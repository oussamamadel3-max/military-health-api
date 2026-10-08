using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Clinical;

public class MedicalExam
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public DateTimeOffset ExaminedAt { get; set; }
    public short ExamSequence { get; set; } = 1;
    public bool IsCurrent { get; set; } = true;
    public int VersionNo { get; set; } = 1;
    public string UpdatedByUserId { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public Clinic Clinic { get; set; } = null!;
    public User ExaminerUser { get; set; } = null!;
    public User UpdatedByUser { get; set; } = null!;
}
