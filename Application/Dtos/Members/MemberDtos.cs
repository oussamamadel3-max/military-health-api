namespace Application.Dtos.Members;

/// <summary>طلب إنشاء ملف منتسب في المركز النشط (بدون CenterId أو أرقام مولّدة أو BMI).</summary>
public class CreateMemberRequest
{
    public string FullName { get; set; } = null!;
    public string MotherName { get; set; } = null!;
    public DateOnly BirthDate { get; set; }
    public short MaritalStatusId { get; set; }
    public string Occupation { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public Guid RecruitmentCenterId { get; set; }
    public short? BloodTypeId { get; set; }
    public decimal? BloodSugarMgDl { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string? BloodPressure { get; set; }
    public short? PulseBpm { get; set; }
    public bool HasTattoo { get; set; }
    public string? DistinguishingMarks { get; set; }
}

/// <summary>طلب تعديل بيانات المنتسب الشخصية والطبية ضمن المركز النشط.</summary>
public class UpdateMemberRequest
{
    public string FullName { get; set; } = null!;
    public string MotherName { get; set; } = null!;
    public DateOnly BirthDate { get; set; }
    public short MaritalStatusId { get; set; }
    public string Occupation { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public Guid RecruitmentCenterId { get; set; }
    public short? BloodTypeId { get; set; }
    public decimal? BloodSugarMgDl { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string? BloodPressure { get; set; }
    public short? PulseBpm { get; set; }
    public bool HasTattoo { get; set; }
    public string? DistinguishingMarks { get; set; }
}

/// <summary>معايير قائمة/بحث المنتسبين مع تصفية زمنية وتصفح.</summary>
public class MemberListQuery
{
    public long? FileNumber { get; set; }
    public string? RecruitmentNumber { get; set; }
    public string? FullName { get; set; }
    public DateOnly? Date { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>عنصر قائمة منتسبين ضمن نطاق المركز.</summary>
public class MemberListItemDto
{
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public int QueueNumber { get; set; }
    public DateOnly QueueDate { get; set; }
    public string FullName { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CurrentMedicalCenterId { get; set; }
    public decimal? Bmi { get; set; }
    public string? BmiCategoryNameAr { get; set; }
}

/// <summary>تفاصيل ملف المنتسب الأساسية (بدون عيادات/استشارات/قرار).</summary>
public class MemberDetailsDto
{
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public int QueueNumber { get; set; }
    public DateOnly QueueDate { get; set; }
    public Guid CurrentMedicalCenterId { get; set; }
    public string? CurrentMedicalCenterNameAr { get; set; }
    public string FullName { get; set; } = null!;
    public string MotherName { get; set; } = null!;
    public DateOnly BirthDate { get; set; }
    public short MaritalStatusId { get; set; }
    public string? MaritalStatusNameAr { get; set; }
    public string Occupation { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public Guid RecruitmentCenterId { get; set; }
    public string? RecruitmentCenterNameAr { get; set; }
    public short? BloodTypeId { get; set; }
    public string? BloodTypeNameAr { get; set; }
    public decimal? BloodSugarMgDl { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? Bmi { get; set; }
    public string? BmiCategoryNameAr { get; set; }
    public string? BloodPressure { get; set; }
    public short? PulseBpm { get; set; }
    public bool HasTattoo { get; set; }
    public string? DistinguishingMarks { get; set; }
    public string ExportStatus { get; set; } = null!;
    public long DataRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
