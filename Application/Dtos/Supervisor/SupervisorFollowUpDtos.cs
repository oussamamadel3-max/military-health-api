namespace Application.Dtos.Supervisor;

/// <summary>
/// ثوابت حالة متابعة ملف المشرف للتصفية والعرض (FR-057).
/// </summary>
public static class SupervisorFollowUpStatuses
{
    /// <summary>عيادات إلزامية مكتملة بلا قرار نهائي — جاهز للاعتماد.</summary>
    public const string Ready = "READY";

    /// <summary>تقييمات إلزامية ناقصة بلا قرار نهائي.</summary>
    public const string Incomplete = "INCOMPLETE";

    /// <summary>قرار نهائي مؤجل (نتيجة 3).</summary>
    public const string Deferred = "DEFERRED";

    /// <summary>قرار نهائي مقبول أو مرفوض دون سجل تعديل.</summary>
    public const string Decided = "DECIDED";

    /// <summary>قرار نهائي معدَّل (وجود FinalDecisionHistory) ونتيجته قبول/رفض.</summary>
    public const string Amended = "AMENDED";
}

/// <summary>معايير بحث وتصفية قائمة متابعة المشرف.</summary>
public class SupervisorFollowUpQuery
{
    public long? FileNumber { get; set; }

    /// <summary>قيمة الباركود الممسوحة (تُفسَّر كرقم ملف).</summary>
    public string? Barcode { get; set; }

    public string? RecruitmentNumber { get; set; }
    public string? FullName { get; set; }

    /// <summary>
    /// حالة الملف: READY | INCOMPLETE | DEFERRED | DECIDED | AMENDED.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>تصفية بالنتيجة النهائية (1/2/3).</summary>
    public short? ResultStatusId { get; set; }

    /// <summary>
    /// تصفية بحالة التصدير: UNEXPORTED | EXPORTED.
    /// </summary>
    public string? ExportStatus { get; set; }

    /// <summary>تصفية بالعيادة الناقصة (لا يوجد تقييم إلزامي لهذه العيادة).</summary>
    public Guid? MissingClinicId { get; set; }

    /// <summary>تصفية بنوع الإجراء المطلوب على DeferredAction.</summary>
    public short? ActionTypeId { get; set; }

    /// <summary>تصفية بحالة الإجراء المؤجل (REQUESTED / COMPLETED / CLOSED).</summary>
    public string? ActionStatus { get; set; }

    /// <summary>تاريخ تسجيل محدد (CreatedAt) — متعارض مع From/To.</summary>
    public DateOnly? Date { get; set; }

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    /// <summary>
    /// الترتيب: UpdatedAtDesc (افتراضي) | UpdatedAtAsc | CreatedAtDesc | FileNumberAsc | DecidedAtDesc.
    /// </summary>
    public string? SortBy { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>عنصر قائمة متابعة المشرف — بلا تفاصيل سريرية تخصصية.</summary>
public class SupervisorFollowUpItemDto
{
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string Barcode { get; set; } = null!;
    public int QueueNumber { get; set; }
    public DateOnly QueueDate { get; set; }
    public string FullName { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ExportStatus { get; set; } = null!;

    public int MandatoryClinicCount { get; set; }
    public int AssessedMandatoryClinicCount { get; set; }
    public bool AreMandatoryClinicsComplete { get; set; }
    public IReadOnlyList<string> MissingClinicCodes { get; set; } = Array.Empty<string>();

    public bool IsReadyToDecide { get; set; }
    public bool CanIssueFinalDecision { get; set; }
    public bool IsDeferred { get; set; }
    public bool IsAmended { get; set; }

    /// <summary>الحالة المحسوبة للشارة في الواجهة.</summary>
    public string FollowUpStatus { get; set; } = null!;

    public Guid? FinalDecisionId { get; set; }
    public short? FinalResultStatusId { get; set; }
    public string? FinalResultStatusNameAr { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? OpenDeferredActionId { get; set; }
    public short? OpenDeferredActionTypeId { get; set; }
    public string? OpenDeferredActionTypeNameAr { get; set; }
    public string? OpenDeferredStatus { get; set; }
    public string? OpenDeferReason { get; set; }
    public DateOnly? OpenDeferUntil { get; set; }
}

/// <summary>صف تصدير Excel لقائمة متابعة المشرف (FR-057).</summary>
public class SupervisorFollowUpExportRowDto
{
    public int QueueNumber { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public string ClinicsCompletionStatusAr { get; set; } = null!;
    public string? FinalResultStatusNameAr { get; set; }
    public string? ResultReason { get; set; }
    public string ExportStatus { get; set; } = null!;
    public string? DeferredActionSummary { get; set; }
}
