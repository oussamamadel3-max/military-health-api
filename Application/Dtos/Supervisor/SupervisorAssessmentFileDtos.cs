using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;

namespace Application.Dtos.Supervisor;

/// <summary>
/// ملف التقييم الطبي المجمّع للمشرف قبل القرار النهائي (FR-051 / FR-052).
/// يعيد استخدام <see cref="MemberMedicalRequestsDto"/> دون عقد عرض موازٍ.
/// </summary>
public class SupervisorAssessmentFileDto
{
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }

    /// <summary>البيانات الشخصية والطبية للمنتسب ضمن المركز النشط.</summary>
    public MemberDetailsDto Member { get; set; } = null!;

    /// <summary>تنبيه الملاحظات العامة عندما يوجد نص غير فارغ.</summary>
    public bool HasGeneralNotesAlert { get; set; }

    /// <summary>أحدث ملاحظة عامة للعرض البارز؛ null عند الخلو.</summary>
    public MemberGeneralNoteItemDto? LatestGeneralNote { get; set; }

    /// <summary>صف لكل عيادة إلزامية (<c>CountsForFinal</c>) ضمن روابط المركز النشطة.</summary>
    public IReadOnlyList<SupervisorClinicAssessmentRowDto> ClinicAssessments { get; set; }
        = Array.Empty<SupervisorClinicAssessmentRowDto>();

    /// <summary>true عند وجود تقييم مسجّل لكل عيادة إلزامية.</summary>
    public bool AreMandatoryClinicsComplete { get; set; }

    /// <summary>
    /// true عندما تكتمل العيادات الإلزامية ولا يوجد قرار نهائي سابق —
    /// لا يحفظ القرار هنا (خارج نطاق البطاقة).
    /// </summary>
    public bool CanIssueFinalDecision { get; set; }

    public IReadOnlyList<string> MissingClinicCodes { get; set; } = Array.Empty<string>();

    /// <summary>سبب منع إصدار القرار عند CanIssueFinalDecision = false؛ null عند السماح.</summary>
    public string? BlockReasonAr { get; set; }

    /// <summary>تنبيهات غير حاجبة (استقصاء معلّق / إجراء مؤجّل مفتوح).</summary>
    public IReadOnlyList<string> WarningsAr { get; set; } = Array.Empty<string>();

    public bool HasPendingInvestigations { get; set; }
    public bool HasOpenDeferredActions { get; set; }

    /// <summary>إجراءات مؤجلة مرتبطة بالمنتسب في المركز (عيادة أو قرار).</summary>
    public IReadOnlyList<SupervisorDeferredActionItemDto> DeferredActions { get; set; }
        = Array.Empty<SupervisorDeferredActionItemDto>();

    /// <summary>عقد المرحلة الخامسة ذاته للاستشارات والاستقصاءات والمرفقات.</summary>
    public MemberMedicalRequestsDto MedicalRequests { get; set; } = null!;

    /// <summary>ملخص القرار النهائي إن وُجد مسبقاً (يمنع إنشاء قرار جديد).</summary>
    public SupervisorFinalDecisionSummaryDto? ExistingFinalDecision { get; set; }
}

/// <summary>صف تقييم عيادة واحدة ضمن الملف المجمّع، مع تمييز الناقص.</summary>
public class SupervisorClinicAssessmentRowDto
{
    public Guid ClinicId { get; set; }
    public string ClinicCode { get; set; } = null!;
    public string ClinicNameAr { get; set; } = null!;
    public short SortOrder { get; set; }
    public bool CountsForFinal { get; set; }

    /// <summary>true عندما لا يوجد تقييم مسجّل لهذه العيادة في المركز.</summary>
    public bool IsMissing { get; set; }

    public bool HasCurrentExam { get; set; }
    public Guid? ExamId { get; set; }
    public Guid? AssessmentId { get; set; }
    public short? ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public string? RejectionReason { get; set; }
    public string? ClinicalNotes { get; set; }
    public string? AssessedByUserId { get; set; }
    public string? AssessedByUserName { get; set; }
    public DateTimeOffset? AssessedAt { get; set; }

    /// <summary>ملخص أحدث إجراء مؤجّل مفتوح مرتبط بتقييم العيادة إن وُجد.</summary>
    public SupervisorClinicDeferredSummaryDto? DeferredAction { get; set; }
}

/// <summary>ملخص إجراء مؤجّل مرتبط بتقييم عيادة.</summary>
public class SupervisorClinicDeferredSummaryDto
{
    public Guid DeferredActionId { get; set; }
    public string DeferReason { get; set; } = null!;
    public short ActionTypeId { get; set; }
    public string? ActionTypeNameAr { get; set; }
    public string? ActionDescription { get; set; }
    public string Status { get; set; } = null!;
    public DateOnly? DeferUntil { get; set; }
    public bool IsOpen { get; set; }
}

/// <summary>عنصر قائمة مؤجلات المنتسب في ملف المشرف.</summary>
public class SupervisorDeferredActionItemDto
{
    public Guid DeferredActionId { get; set; }
    public Guid? ClinicId { get; set; }
    public string? ClinicNameAr { get; set; }
    public Guid? ClinicAssessmentId { get; set; }
    public Guid? FinalDecisionId { get; set; }
    public string DeferReason { get; set; } = null!;
    public short ActionTypeId { get; set; }
    public string? ActionTypeNameAr { get; set; }
    public string? ActionDescription { get; set; }
    public string FollowupNote { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateOnly? DeferUntil { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public string? RequestedByUserName { get; set; }
    public string? CompletedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool IsOverdue { get; set; }
}

/// <summary>ملخص قرار نهائي موجود مسبقاً (عرض فقط في هذه البطاقة).</summary>
public class SupervisorFinalDecisionSummaryDto
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
}
