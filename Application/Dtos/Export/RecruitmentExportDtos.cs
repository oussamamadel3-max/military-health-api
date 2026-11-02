namespace Application.Dtos.Export;

/// <summary>معايير قائمة الملفات الجاهزة للتصدير للتجنيد (FR-058).</summary>
public class RecruitmentExportPendingQuery
{
    public long? FileNumber { get; set; }
    public string? RecruitmentNumber { get; set; }
    public string? FullName { get; set; }

    /// <summary>تصفية بالنتيجة النهائية: 1 مقبول أو 2 مرفوض فقط.</summary>
    public short? ResultStatusId { get; set; }

    public DateOnly? DecisionFrom { get; set; }
    public DateOnly? DecisionTo { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>عنصر قائمة الانتظار للتصدير.</summary>
public class RecruitmentExportPendingItemDto
{
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string RecruitmentNumber { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public Guid RecruitmentCenterId { get; set; }
    public string RecruitmentCenterCode { get; set; } = null!;
    public string RecruitmentCenterNameAr { get; set; } = null!;
    public Guid FinalDecisionId { get; set; }
    public short ResultStatusId { get; set; }
    public string ResultStatusNameAr { get; set; } = null!;
    public string? ResultReason { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public long DataRevision { get; set; }
    public bool IsReexportCandidate { get; set; }
}

/// <summary>طلب إنشاء دفعة تصدير محددة أو كل المعلقة (FR-059).</summary>
public class CreateRecruitmentExportBatchRequest
{
    /// <summary>معرفات منتسبين محددين. يُتجاهل عند ExportAllPending=true.</summary>
    public IReadOnlyList<Guid>? MemberIds { get; set; }

    /// <summary>تصدير جميع الملفات المعلقة المطابقة لفلاتر القائمة.</summary>
    public bool ExportAllPending { get; set; }

    public short? ResultStatusId { get; set; }
    public DateOnly? DecisionFrom { get; set; }
    public DateOnly? DecisionTo { get; set; }
    public long? FileNumber { get; set; }
    public string? RecruitmentNumber { get; set; }
    public string? FullName { get; set; }
}

/// <summary>نتيجة إنشاء دفعة التصدير بعد اكتمال التوليد أو الفشل.</summary>
public class RecruitmentExportBatchResultDto
{
    public Guid ExportBatchId { get; set; }
    public string Status { get; set; } = null!;
    public int RecordCount { get; set; }
    public int ExcludedCount { get; set; }
    public IReadOnlyList<RecruitmentExportExclusionDto> Exclusions { get; set; } = Array.Empty<RecruitmentExportExclusionDto>();
    public Guid? GeneratedFileId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? DownloadPath { get; set; }
}

/// <summary>ملف استُبعد أثناء إنشاء الدفعة (فقد الأهلية / قرار مؤجل / مكرّر).</summary>
public class RecruitmentExportExclusionDto
{
    public Guid MemberId { get; set; }
    public string ReasonAr { get; set; } = null!;
}

/// <summary>معايير سجل دفعات التصدير.</summary>
public class RecruitmentExportBatchQuery
{
    public string? Status { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>ملخص دفعة تصدير في السجل.</summary>
public class RecruitmentExportBatchSummaryDto
{
    public Guid ExportBatchId { get; set; }
    public string Status { get; set; } = null!;
    public int RecordCount { get; set; }
    public string CenterCodeSnapshot { get; set; } = null!;
    public string CenterNameSnapshot { get; set; } = null!;
    public string ExportedByUserId { get; set; } = null!;
    public Guid? GeneratedFileId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? DownloadPath { get; set; }
}

/// <summary>تفاصيل دفعة مع عناصرها.</summary>
public class RecruitmentExportBatchDetailDto : RecruitmentExportBatchSummaryDto
{
    public IReadOnlyList<RecruitmentExportItemDto> Items { get; set; } = Array.Empty<RecruitmentExportItemDto>();
}

/// <summary>عنصر داخل دفعة تصدير (لقطة تاريخية).</summary>
public class RecruitmentExportItemDto
{
    public Guid ExportBatchId { get; set; }
    public Guid MemberId { get; set; }
    public Guid FinalDecisionId { get; set; }
    public long FileNumberSnapshot { get; set; }
    public string RecruitmentNumberSnapshot { get; set; } = null!;
    public string? FullName { get; set; }
    public short ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public string? ResultReasonSnapshot { get; set; }
    public DateTimeOffset DecisionAtSnapshot { get; set; }
    public long DataRevisionSnapshot { get; set; }
    public bool IsReexport { get; set; }
    public Guid? PreviousExportBatchId { get; set; }
    public DateTimeOffset ExportedAt { get; set; }
    public Guid RecruitmentCenterId { get; set; }
    public string RecruitmentCenterCodeSnapshot { get; set; } = null!;
    public string RecruitmentCenterNameSnapshot { get; set; } = null!;
}

/// <summary>معايير سجل العناصر المصدَّرة تاريخياً (FR-060).</summary>
public class RecruitmentExportHistoryQuery
{
    public long? FileNumber { get; set; }
    public string? RecruitmentNumber { get; set; }
    public string? FullName { get; set; }
    public short? ResultStatusId { get; set; }
    public DateOnly? ExportedFrom { get; set; }
    public DateOnly? ExportedTo { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>صف سجل تاريخي لملف مصدَّر.</summary>
public class RecruitmentExportHistoryItemDto
{
    public Guid ExportBatchId { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public short ResultStatusId { get; set; }
    public string ResultStatusNameAr { get; set; } = null!;
    public string? ResultReason { get; set; }
    public DateTimeOffset DecisionAt { get; set; }
    public DateTimeOffset ExportedAt { get; set; }
    public string ExportedByUserId { get; set; } = null!;
    public bool IsReexport { get; set; }
    public Guid? PreviousExportBatchId { get; set; }
    public string BatchStatus { get; set; } = null!;
}

/// <summary>صف Excel لدفعة التصدير الرسمية المؤقتة (CON-08 / FR-059).</summary>
public class RecruitmentExportExcelRowDto
{
    public string CenterCode { get; set; } = null!;
    public string CenterName { get; set; } = null!;
    public long FileNumber { get; set; }
    public string RecruitmentNumber { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string RecruitmentCenterCode { get; set; } = null!;
    public string RecruitmentCenterName { get; set; } = null!;
    public string ResultNameAr { get; set; } = null!;
    public string? ResultReason { get; set; }
    public DateTimeOffset DecisionAt { get; set; }
}

/// <summary>صف Excel لسجل التصدير التاريخي.</summary>
public class RecruitmentExportHistoryExcelRowDto
{
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public string ResultNameAr { get; set; } = null!;
    public DateTimeOffset DecisionAt { get; set; }
    public DateTimeOffset ExportedAt { get; set; }
    public string ExportedByUserId { get; set; } = null!;
    public bool IsReexport { get; set; }
}
