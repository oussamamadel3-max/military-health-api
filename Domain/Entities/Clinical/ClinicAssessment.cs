using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Clinical;

public class ClinicAssessment
{
    public Guid AssessmentId { get; set; }
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public short ResultStatusId { get; set; }
    public string? RejectionReason { get; set; }
    public string? ClinicalNotes { get; set; }
    public string AssessedByUserId { get; set; } = null!;
    public DateTimeOffset AssessedAt { get; set; }
    public string UpdatedByUserId { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }

    public MedicalExam Exam { get; set; } = null!;
    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public Clinic Clinic { get; set; } = null!;
    public ResultStatus ResultStatus { get; set; } = null!;
    public User AssessedByUser { get; set; } = null!;
    public User UpdatedByUser { get; set; } = null!;
}
