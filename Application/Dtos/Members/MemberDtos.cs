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

    /// <summary>
    /// سبب التعديل؛ إلزامي فقط عند وجود قرار نهائي معتمد وصلاحية <c>Members.EditAfterFinalDecision</c>.
    /// يُتجاهل قبل القرار النهائي ولا يُقبل كتجاوز عند غياب الصلاحية الإدارية.
    /// </summary>
    public string? EditReason { get; set; }
}

/// <summary>معايير قائمة/بحث المنتسبين مع تصفية زمنية وتصفح.</summary>
public class MemberListQuery
{
    public long? FileNumber { get; set; }
    /// <summary>قيمة الباركود الممسوحة (تُفسَّر كرقم ملف).</summary>
    public string? Barcode { get; set; }
    public string? RecruitmentNumber { get; set; }
    public string? FullName { get; set; }
    /// <summary>رقم الدور ضمن مركز الدور وتاريخه الحالي للمنتسب.</summary>
    public int? QueueNumber { get; set; }
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

/// <summary>
/// تفاصيل ملف المنتسب الشخصية والطبية مع الملاحظات والنقل.
/// القيم الاختيارية غير المحددة تُرجع <c>null</c> دون إخفاء الحقل (عيادات/استشارات/قرار خارج هذه البطاقة).
/// </summary>
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

    /// <summary>تنبيه الملاحظات العامة عندما توجد ملاحظات نشطة غير فارغة.</summary>
    public bool HasGeneralNotesAlert { get; set; }

    /// <summary>الملاحظات العامة النشطة؛ قائمة فارغة عند عدم وجود ملاحظات.</summary>
    public IReadOnlyList<MemberGeneralNoteItemDto> GeneralNotes { get; set; } = Array.Empty<MemberGeneralNoteItemDto>();

    /// <summary>آخر عملية نقل إن وُجدت؛ <c>null</c> إن لم يُنقل الملف.</summary>
    public MemberTransferSummaryDto? LastTransfer { get; set; }
}

/// <summary>عنصر ملاحظة عامة في تفاصيل المنتسب.</summary>
public class MemberGeneralNoteItemDto
{
    public Guid NoteId { get; set; }
    public string NoteText { get; set; } = null!;
    public string AuthorUserId { get; set; } = null!;
    public string? AuthorUserName { get; set; }
    public Guid AuthorClinicId { get; set; }
    public string? AuthorClinicNameAr { get; set; }
    public DateTimeOffset WrittenAt { get; set; }
}

/// <summary>ملخص آخر نقل لملف المنتسب.</summary>
public class MemberTransferSummaryDto
{
    public Guid TransferId { get; set; }
    public Guid FromCenterId { get; set; }
    public string? FromCenterNameAr { get; set; }
    public Guid ToCenterId { get; set; }
    public string? ToCenterNameAr { get; set; }
    public string TransferReason { get; set; } = null!;
    public DateTimeOffset ExecutedAt { get; set; }
}
