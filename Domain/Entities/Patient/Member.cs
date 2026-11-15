using Domain.Attributes;
using Domain.Entities.Auth;
using Domain.Entities.Core;

namespace Domain.Entities.Patient;

/// <summary>
/// ملف المنتسب. ضمن كتالوج صلاحيات الحقول التجريبي (Phase B): الحقل المعتمد حالياً MotherName فقط عبر MemberDetailsDto.
/// </summary>
[FieldPermissionEntity("المنتسب")]
public class Member
{
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string RecruitmentNumber { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string MotherName { get; set; } = null!;
    public DateOnly BirthDate { get; set; }
    public short MaritalStatusId { get; set; }
    public string Occupation { get; set; } = null!;
    public Guid RecruitmentCenterId { get; set; }
    public short? BloodTypeId { get; set; }
    public decimal? BloodSugarMgDl { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? Bmi { get; set; }
    public string? BloodPressure { get; set; }
    public short? PulseBpm { get; set; }
    public bool HasTattoo { get; set; }
    public string? DistinguishingMarks { get; set; }
    public Guid CurrentMedicalCenterId { get; set; }
    public Guid QueueMedicalCenterId { get; set; }
    public DateOnly QueueDate { get; set; }
    public int QueueNumber { get; set; }
    public string ExportStatus { get; set; } = "UNEXPORTED";
    public long DataRevision { get; set; } = 1;
    public long? LastExportedRevision { get; set; }
    public DateTimeOffset? LastExportedAt { get; set; }
    public string CreatedByUserId { get; set; } = null!;
    public string UpdatedByUserId { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public long? ReviewedRevision { get; set; }

    public MaritalStatus MaritalStatus { get; set; } = null!;
    public RecruitmentCenter RecruitmentCenter { get; set; } = null!;
    public BloodType? BloodType { get; set; }
    public MedicalCenter CurrentMedicalCenter { get; set; } = null!;
    public MedicalCenter QueueMedicalCenter { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public User UpdatedByUser { get; set; } = null!;
    public User? ReviewedByUser { get; set; }
}
