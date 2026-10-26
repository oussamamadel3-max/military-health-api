namespace Application.Dtos.ClinicWorkspace;

/// <summary>رموز حالة فحص العيادة لمساحة عمل الطبيب (FR-032 / شريحة فتح FR-045).</summary>
public static class ClinicExamStatusCodes
{
    public const string NotRegistered = "NotRegistered";
    public const string Registered = "Registered";
    public const string Deferred = "Deferred";
    public const string InEdit = "InEdit";
}

/// <summary>إسناد عيادة نشط للطبيب الحالي ضمن المركز النشط.</summary>
public class DoctorClinicDto
{
    public Guid ClinicId { get; set; }
    public string ClinicCode { get; set; } = null!;
    public string ClinicNameAr { get; set; } = null!;
    public int ClinicSortOrder { get; set; }
    public string? SpecialtyTitle { get; set; }
}

/// <summary>نتيجة بحث ضمن سياق مساحة عمل العيادة.</summary>
public class ClinicMemberSearchItemDto
{
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public int QueueNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public string Barcode { get; set; } = null!;
    public string ClinicExamStatus { get; set; } = null!;
}

/// <summary>ملخص ملف المنتسب بعد فتحه في سياق العيادة.</summary>
public class ClinicMemberSummaryDto
{
    public Guid MemberId { get; set; }
    public Guid ClinicId { get; set; }
    public string FullName { get; set; } = null!;
    public long FileNumber { get; set; }
    public string RecruitmentNumber { get; set; } = null!;
    public string? RecruitmentCenterNameAr { get; set; }
    public DateOnly BirthDate { get; set; }
    public int AgeYears { get; set; }
    public int QueueNumber { get; set; }
    public string Barcode { get; set; } = null!;
    public string ClinicExamStatus { get; set; } = null!;
    public bool CanAcquireEditLock { get; set; }
    public ClinicMemberEditLockDto? EditLock { get; set; }
    public ClinicDeferredSummaryDto? DeferredSummary { get; set; }
    public ClinicGeneralNoteDto? LatestGeneralNote { get; set; }
}

/// <summary>قفل تحرير نشط بحوزة طبيب آخر أو الطبيب الحالي.</summary>
public class ClinicMemberEditLockDto
{
    public string LockedByUserId { get; set; } = null!;
    public string? LockedByUserName { get; set; }
    public DateTimeOffset LockedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsOwnedByCurrentUser { get; set; }
}

/// <summary>ملخص إجراء مؤجّل مفتوح لتقييم عيادة مؤجّل.</summary>
public class ClinicDeferredSummaryDto
{
    public string DeferReason { get; set; } = null!;
    public short? ActionTypeId { get; set; }
    public string? ActionTypeNameAr { get; set; }
    public string? ActionDescription { get; set; }
    public string? Status { get; set; }
}

/// <summary>أحدث ملاحظة عامة نشطة لعرض التنبيه.</summary>
public class ClinicGeneralNoteDto
{
    public Guid NoteId { get; set; }
    public string NoteText { get; set; } = null!;
    public string AuthorUserId { get; set; } = null!;
    public string? AuthorUserName { get; set; }
    public Guid AuthorClinicId { get; set; }
    public string? AuthorClinicNameAr { get; set; }
    public DateTimeOffset WrittenAt { get; set; }
}
