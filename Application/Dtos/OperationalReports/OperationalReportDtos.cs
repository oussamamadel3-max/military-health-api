namespace Application.Dtos.OperationalReports;

/// <summary>نطاق زمني شامل للطرفين (From/To كتاريخ تقويمي).</summary>
public class OperationalDateRangeQuery
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public DateOnly? Date { get; set; }
}

/// <summary>فلاتر لوحة مؤشرات المركز (FR-062) — أعداد حسب الفترة/العيادة/النتيجة/التصدير.</summary>
public class LocalKpiQuery : OperationalDateRangeQuery
{
    public Guid? ClinicId { get; set; }
    public short? ResultStatusId { get; set; }
    public string? ExportStatus { get; set; }
}

/// <summary>فلاتر المؤشرات المركزية (FR-063).</summary>
public class CentralKpiQuery : OperationalDateRangeQuery
{
    /// <summary>مراكز مشمولة؛ فارغ = كل المراكز النشطة.</summary>
    public IReadOnlyList<Guid>? CenterIds { get; set; }

    public short? ResultStatusId { get; set; }
}

/// <summary>لوحة مؤشرات مركز واحد.</summary>
public class CenterKpiDto
{
    public Guid CenterId { get; set; }
    public string CenterCode { get; set; } = null!;
    public string CenterNameAr { get; set; } = null!;
    public int TotalMembers { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int DeferredCount { get; set; }
    public int WaitingCount { get; set; }
    public int ExportedCount { get; set; }
    public int UnexportedCount { get; set; }
    public int CompleteExamsCount { get; set; }
    public int IncompleteExamsCount { get; set; }
    public IReadOnlyList<DeferredActionBreakdownDto> DeferredByAction { get; set; } =
        Array.Empty<DeferredActionBreakdownDto>();
}

/// <summary>تجميع مركزي مع منهجية منع الاحتساب المزدوج.</summary>
public class CentralKpiResponseDto
{
    /// <summary>
    /// المنهجية المؤقتة حتى اكتمال نقل الملفات (T-11): احتساب كل منتسب مرة واحدة عبر CurrentMedicalCenterId.
    /// </summary>
    public string AntiDoubleCountMethodology { get; set; } =
        "CurrentMedicalCenterId — each member counted once at current center";

    public CenterKpiDto Totals { get; set; } = null!;
    public IReadOnlyList<CenterKpiDto> Centers { get; set; } = Array.Empty<CenterKpiDto>();
}

public class DeferredActionBreakdownDto
{
    public short ActionTypeId { get; set; }
    public string ActionTypeCode { get; set; } = null!;
    public string ActionTypeNameAr { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int Count { get; set; }
    public int LateCount { get; set; }
}

/// <summary>
/// فلاتر القائمة النهائية (FR-064). عند تعيين الفترة تُطبَّق على
/// <c>FinalDecision.DecidedAt</c> لقرار نفس المركز، وليس على <c>Member.CreatedAt</c>.
/// </summary>
public class FinalMembersQuery : OperationalDateRangeQuery
{
    public short? ResultStatusId { get; set; }
    public Guid? ClinicId { get; set; }
    public string? ExportStatus { get; set; }
    public bool? IsArchived { get; set; }
    public string? Search { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// فلاتر قائمة قيد الانتظار (تفصيل <c>waitingCount</c> من FR-062).
/// الفترة على <c>Member.CreatedAt</c>؛ المنتسب في المركز الحالي بلا قرار نهائي لنفس المركز.
/// </summary>
public class WaitingMembersQuery : OperationalDateRangeQuery
{
    public Guid? ClinicId { get; set; }
    public string? ExportStatus { get; set; }
    public bool? IsArchived { get; set; }
    public string? Search { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class FinalMemberListItemDto
{
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public short? FinalResultStatusId { get; set; }
    public string? FinalResultNameAr { get; set; }
    public string? FinalResultReason { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateOnly? DeferUntil { get; set; }
    public string? OpenDeferredActionSummary { get; set; }
    public string ExportStatus { get; set; } = null!;
    public bool IsArchived { get; set; }
    public string? ArchiveNumber { get; set; }
    public string? LatestGeneralNote { get; set; }
    public IReadOnlyList<FinalMemberClinicSummaryDto> ClinicAssessments { get; set; } =
        Array.Empty<FinalMemberClinicSummaryDto>();
}

public class FinalMemberClinicSummaryDto
{
    public Guid ClinicId { get; set; }
    public string ClinicCode { get; set; } = null!;
    public string ClinicNameAr { get; set; } = null!;
    public short? ResultStatusId { get; set; }
    public string? ResultNameAr { get; set; }
    public string? ExaminerUserId { get; set; }
    public string? ExaminerDisplayName { get; set; }
    public DateTimeOffset? AssessedAt { get; set; }
}

/// <summary>فلاتر تقرير المؤجلات (FR-065).</summary>
public class DeferredReportQuery : OperationalDateRangeQuery
{
    public short? ActionTypeId { get; set; }
    public string? ActionStatus { get; set; }
    public bool? LateOnly { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class DeferredReportItemDto
{
    public Guid DeferredActionId { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public Guid CenterId { get; set; }
    public short ActionTypeId { get; set; }
    public string ActionTypeCode { get; set; } = null!;
    public string ActionTypeNameAr { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string DeferReason { get; set; } = null!;
    public string? ActionDescription { get; set; }
    public DateOnly? DeferUntil { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public bool IsLate { get; set; }
    public bool IsClinicLevel { get; set; }
    public bool IsFinalDecisionLevel { get; set; }
    public Guid? ClinicId { get; set; }
}

public class DeferredReportResponseDto
{
    public int TotalCount { get; set; }
    public int LateCount { get; set; }
    public IReadOnlyList<DeferredActionBreakdownDto> Breakdown { get; set; } =
        Array.Empty<DeferredActionBreakdownDto>();
    public IReadOnlyList<DeferredReportItemDto> Items { get; set; } =
        Array.Empty<DeferredReportItemDto>();
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// تقرير محلي بالأعداد والنسب (FR-066).
/// عند تعيين الفترة: أعداد القرار النهائي (مقبول/مرفوض/مؤجل) على <c>DecidedAt</c>؛
/// الانتظار على <c>CreatedAt</c> لمن بلا قرار؛ تفصيل العيادات على <c>AssessedAt</c>.
/// </summary>
public class LocalSummaryQuery : OperationalDateRangeQuery
{
    public short? ResultStatusId { get; set; }
    public Guid? ClinicId { get; set; }
}

public class LocalSummaryDto
{
    public Guid CenterId { get; set; }
    public string CenterCode { get; set; } = null!;
    public string CenterNameAr { get; set; } = null!;
    public int TotalMembers { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int DeferredCount { get; set; }
    public int WaitingCount { get; set; }
    public decimal AcceptedPercent { get; set; }
    public decimal RejectedPercent { get; set; }
    public decimal DeferredPercent { get; set; }
    public decimal WaitingPercent { get; set; }
    public IReadOnlyList<ClinicResultCountDto> ByClinic { get; set; } =
        Array.Empty<ClinicResultCountDto>();
}

public class ClinicResultCountDto
{
    public Guid ClinicId { get; set; }
    public string ClinicCode { get; set; } = null!;
    public string ClinicNameAr { get; set; } = null!;
    public int AssessmentCount { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int DeferredCount { get; set; }
}

/// <summary>مقارنة مراكز (FR-067).</summary>
public class CentralCompareQuery : OperationalDateRangeQuery
{
    public IReadOnlyList<Guid>? CenterIds { get; set; }
    public short? ResultStatusId { get; set; }
    public string? ExportStatus { get; set; }
    public bool? IsArchived { get; set; }
}

public class CentralCompareResponseDto
{
    public string AntiDoubleCountMethodology { get; set; } =
        "CurrentMedicalCenterId — each member counted once at current center";

    public IReadOnlyList<CentralCompareCenterDto> Centers { get; set; } =
        Array.Empty<CentralCompareCenterDto>();

    public CentralCompareCenterDto Totals { get; set; } = null!;
}

public class CentralCompareCenterDto
{
    public Guid CenterId { get; set; }
    public string CenterCode { get; set; } = null!;
    public string CenterNameAr { get; set; } = null!;
    public int TotalMembers { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int DeferredCount { get; set; }
    public int WaitingCount { get; set; }
    public int ExportedCount { get; set; }
    public int UnexportedCount { get; set; }
    public int ArchivedCount { get; set; }
}

/// <summary>تقرير ربط الأطباء (FR-068). الفترة على ExaminedAt؛ ResultStatusId يصفي نتيجة تقييم العيادة.</summary>
public class DoctorLinkageQuery : OperationalDateRangeQuery
{
    public Guid? ClinicId { get; set; }

    /// <summary>تصفية بنتيجة تقييم العيادة (1/2/3)، وليس القرار النهائي.</summary>
    public short? ResultStatusId { get; set; }

    /// <summary>BY_MEMBER | BY_DOCTOR</summary>
    public string ViewMode { get; set; } = DoctorLinkageViewModes.ByMember;

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public static class DoctorLinkageViewModes
{
    public const string ByMember = "BY_MEMBER";
    public const string ByDoctor = "BY_DOCTOR";
}

public class DoctorLinkageByMemberItemDto
{
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    /// <summary>نتيجة القرار النهائي في مركز التقرير إن وُجد.</summary>
    public short? FinalResultStatusId { get; set; }
    public string? FinalResultNameAr { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public IReadOnlyList<DoctorExamLinkDto> Exams { get; set; } = Array.Empty<DoctorExamLinkDto>();
}

public class DoctorExamLinkDto
{
    public string ExaminerUserId { get; set; } = null!;
    public string? ExaminerDisplayName { get; set; }
    public Guid ClinicId { get; set; }
    public string ClinicCode { get; set; } = null!;
    public string ClinicNameAr { get; set; } = null!;
    public DateTimeOffset ExaminedAt { get; set; }
    public short? ResultStatusId { get; set; }
    public string? ResultNameAr { get; set; }
}

public class DoctorLinkageByDoctorItemDto
{
    public string ExaminerUserId { get; set; } = null!;
    public string? ExaminerDisplayName { get; set; }
    public Guid? ClinicId { get; set; }
    public string? ClinicCode { get; set; }
    public string? ClinicNameAr { get; set; }
    public int MemberCount { get; set; }
    public int ExamCount { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int DeferredCount { get; set; }
}

/// <summary>صف Excel مبسّط للمؤشرات.</summary>
public class CenterKpiExcelRowDto
{
    public string CenterCode { get; set; } = null!;
    public string CenterNameAr { get; set; } = null!;
    public int TotalMembers { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int DeferredCount { get; set; }
    public int WaitingCount { get; set; }
    public int ExportedCount { get; set; }
    public int UnexportedCount { get; set; }
}

public class FinalMemberExcelRowDto
{
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public string? FinalResultNameAr { get; set; }
    public string? FinalResultReason { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string ExportStatus { get; set; } = null!;
    public bool IsArchived { get; set; }
}

public class DeferredExcelRowDto
{
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string ActionTypeNameAr { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateOnly? DeferUntil { get; set; }
    public bool IsLate { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
}

public class DoctorLinkageExcelRowDto
{
    public long? FileNumber { get; set; }
    public string? MemberName { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public string? ExaminerDisplayName { get; set; }
    public string? ClinicCode { get; set; }
    public DateTimeOffset? ExaminedAt { get; set; }
    public string? ResultNameAr { get; set; }
    public int? MemberCount { get; set; }
}
