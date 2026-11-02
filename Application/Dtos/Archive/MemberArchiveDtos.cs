using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
using Application.Dtos.Supervisor;

namespace Application.Dtos.Archive;

/// <summary>معايير البحث في الملفات المؤرشفة (FR-070).</summary>
public class ArchiveSearchQuery
{
    /// <summary>الرقم الأرشيفي (تطابق جزئي غير حسّاس لحالة الأحرف).</summary>
    public string? ArchiveNumber { get; set; }

    public long? FileNumber { get; set; }

    public string? FullName { get; set; }

    public string? RecruitmentNumber { get; set; }

    /// <summary>تصفية بمركز الأرشفة؛ للمسار المركزي فقط. المسار المحلي يتجاهله ويستخدم المركز النشط.</summary>
    public Guid? CenterId { get; set; }

    /// <summary>تصفية بنتيجة القرار النهائي (1/2/3).</summary>
    public short? ResultStatusId { get; set; }

    public DateOnly? DecisionFrom { get; set; }
    public DateOnly? DecisionTo { get; set; }
    public DateOnly? ArchivedFrom { get; set; }
    public DateOnly? ArchivedTo { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>صف قائمة أرشيف للبحث قبل فتح التقرير الكامل.</summary>
public class ArchiveSearchItemDto
{
    public Guid ArchiveId { get; set; }
    public string ArchiveNumber { get; set; } = null!;
    public DateTimeOffset ArchivedAt { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string RecruitmentNumber { get; set; } = null!;
    public Guid CenterId { get; set; }
    public string? CenterCode { get; set; }
    public string? CenterNameAr { get; set; }
    public Guid FinalDecisionId { get; set; }
    public short ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}

/// <summary>التقرير الطبي الكامل للمنتسب المؤرشف (FR-071).</summary>
public class MemberFullMedicalReportDto
{
    public Guid ArchiveId { get; set; }
    public string ArchiveNumber { get; set; } = null!;
    public DateTimeOffset ArchivedAt { get; set; }
    public DateTimeOffset ArchiveUpdatedAt { get; set; }

    public Guid ArchiveCenterId { get; set; }
    public string? ArchiveCenterCode { get; set; }
    public string? ArchiveCenterNameAr { get; set; }

    /// <summary>بيانات المنتسب الشخصية والطبية مع الملاحظات وآخر نقل إن وُجد.</summary>
    public MemberDetailsDto Member { get; set; } = null!;

    /// <summary>تقييمات العيادات الإلزامية في مركز الأرشفة مع الأطباء.</summary>
    public IReadOnlyList<SupervisorClinicAssessmentRowDto> ClinicAssessments { get; set; }
        = Array.Empty<SupervisorClinicAssessmentRowDto>();

    /// <summary>الاستشارات والاستقصاءات والمرفقات (بدون StorageKey).</summary>
    public MemberMedicalRequestsDto MedicalRequests { get; set; } = null!;

    public IReadOnlyList<MemberGeneralNoteItemDto> GeneralNotes { get; set; }
        = Array.Empty<MemberGeneralNoteItemDto>();

    public IReadOnlyList<SupervisorDeferredActionItemDto> DeferredActions { get; set; }
        = Array.Empty<SupervisorDeferredActionItemDto>();

    public ArchiveFinalDecisionSectionDto FinalDecision { get; set; } = null!;

    /// <summary>تاريخ تعديل القرار مرتباً من الأقدم؛ فارغ إن لم يُعدَّل.</summary>
    public IReadOnlyList<ArchiveDecisionHistoryItemDto> DecisionHistory { get; set; }
        = Array.Empty<ArchiveDecisionHistoryItemDto>();

    /// <summary>true عندما يتوفر قرار نهائي مرتبط بالأرشيف.</summary>
    public bool IsComplete { get; set; }

    /// <summary>سبب عدم الاكتمال عند IsComplete = false؛ null عند الاكتمال.</summary>
    public string? IncompleteReasonAr { get; set; }
}

/// <summary>قسم القرار النهائي ضمن التقرير الكامل.</summary>
public class ArchiveFinalDecisionSectionDto
{
    public Guid FinalDecisionId { get; set; }
    public short ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public string? RejectionReason { get; set; }
    public string? DisagreementJustification { get; set; }
    public string SupervisorUserId { get; set; } = null!;
    public string? SupervisorUserName { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>الإجراء المؤجل المرتبط بالقرار النهائي إن وُجد.</summary>
    public SupervisorDeferredActionItemDto? LinkedDeferredAction { get; set; }
}

/// <summary>سجل تعديل قرار نهائي للعرض في التقرير (FR-071 / FR-056).</summary>
public class ArchiveDecisionHistoryItemDto
{
    public Guid HistoryId { get; set; }
    public short OldStatusId { get; set; }
    public string? OldStatusNameAr { get; set; }
    public short NewStatusId { get; set; }
    public string? NewStatusNameAr { get; set; }
    public string ChangeReason { get; set; } = null!;
    public string ChangedByUserId { get; set; } = null!;
    public string? ChangedByUserName { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public bool ExportResetPerformed { get; set; }
}
