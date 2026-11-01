namespace Application.Dtos.Supervisor;

/// <summary>
/// طلب إصدار القرار النهائي المعتمد (قبول/رفض/مؤجل).
/// التأجيل يتطلب حقول الإجراء المطلوب ومدة الانتهاء (FR-055).
/// </summary>
public class IssueFinalDecisionRequest
{
    /// <summary>1=مقبول، 2=مرفوض، 3=مؤجل.</summary>
    public short ResultStatusId { get; set; }

    /// <summary>سبب الرفض إلزامي عندما ResultStatusId = 2؛ يجب أن يكون فارغاً لغير الرفض.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>مبرر الاختلاف عن تقييم عيادة أو أكثر؛ إلزامي عند التعارض.</summary>
    public string? DisagreementJustification { get; set; }

    /// <summary>سبب التأجيل؛ إلزامي عندما ResultStatusId = 3.</summary>
    public string? DeferReason { get; set; }

    /// <summary>تاريخ انتهاء التأجيل؛ إلزامي للقرار النهائي المؤجل.</summary>
    public DateOnly? DeferUntil { get; set; }

    /// <summary>نوع الإجراء المطلوب؛ إلزامي عند التأجيل.</summary>
    public short? ActionTypeId { get; set; }

    /// <summary>توصيف الإجراء عند الأنواع التي تتطلب وصفاً (مثل غير ذلك).</summary>
    public string? ActionDescription { get; set; }

    /// <summary>ملاحظة توضيحية إلزامية عند التأجيل.</summary>
    public string? FollowupNote { get; set; }

    /// <summary>العيادة المستهدفة عند الأنواع التي تتطلب عيادة.</summary>
    public Guid? TargetClinicId { get; set; }

    /// <summary>
    /// مراجعة بيانات المنتسب المتوقعة للحماية من اعتماد متزامن.
    /// عند الإرسال وعدم التطابق يُرفض الطلب بـ 409.
    /// </summary>
    public long? ExpectedDataRevision { get; set; }
}

/// <summary>
/// طلب تعديل القرار النهائي القائم دون إنشاء قرار ثانٍ (FR-056).
/// يرث حقول النتيجة من الإصدار ويضيف سبب التعديل الإلزامي.
/// </summary>
public class AmendFinalDecisionRequest : IssueFinalDecisionRequest
{
    /// <summary>سبب التعديل؛ إلزامي دائماً عند تعديل قرار سابق.</summary>
    public string ChangeReason { get; set; } = null!;
}

/// <summary>نتيجة إصدار أو تعديل القرار النهائي المعتمد.</summary>
public class FinalDecisionDto
{
    public Guid FinalDecisionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public short ResultStatusId { get; set; }
    public string? ResultStatusNameAr { get; set; }
    public string? RejectionReason { get; set; }
    public string? DisagreementJustification { get; set; }
    public string SupervisorUserId { get; set; } = null!;
    public DateTimeOffset DecidedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long MemberDataRevision { get; set; }

    /// <summary>رموز العيادات التي اختلف عنها القرار (فارغة عند التوافق الكامل).</summary>
    public IReadOnlyList<string> DisagreeingClinicCodes { get; set; } = Array.Empty<string>();

    /// <summary>عدد سجلات اعتراض المشرف المُنشأة لهذا القرار في هذه العملية.</summary>
    public int ObjectionCount { get; set; }

    /// <summary>معرّف الإجراء المؤجل المرتبط عند النتيجة مؤجل؛ null لغير ذلك.</summary>
    public Guid? DeferredActionId { get; set; }

    /// <summary>ملخص الإجراء المؤجل للعرض في متابعة المشرف.</summary>
    public FinalDecisionDeferredActionDto? DeferredAction { get; set; }

    /// <summary>
    /// نقطة ربط لأرشفة القرار (T-10 / BR-017). false لأن الأرشفة خارج نطاق بطاقات القرار الحالية.
    /// </summary>
    public bool ArchiveCreated { get; set; }
}

/// <summary>نتيجة تعديل القرار النهائي مع معرف سجل التاريخ وإعادة التصدير.</summary>
public class AmendFinalDecisionDto : FinalDecisionDto
{
    public Guid HistoryId { get; set; }
    public short OldResultStatusId { get; set; }
    public string ChangeReason { get; set; } = null!;
    public bool ExportResetPerformed { get; set; }
    public string? PreviousExportStatus { get; set; }
}

/// <summary>عنصر في سجل تغيّر النتيجة النهائية (BR-022).</summary>
public class FinalDecisionHistoryItemDto
{
    public Guid HistoryId { get; set; }
    public Guid FinalDecisionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public short OldStatusId { get; set; }
    public string? OldStatusNameAr { get; set; }
    public short NewStatusId { get; set; }
    public string? NewStatusNameAr { get; set; }
    public string ChangeReason { get; set; } = null!;
    public string ChangedByUserId { get; set; } = null!;
    public DateTimeOffset ChangedAt { get; set; }
    public bool ExportResetPerformed { get; set; }
}

/// <summary>ملخص إجراء مؤجل مرتبط بقرار نهائي.</summary>
public class FinalDecisionDeferredActionDto
{
    public Guid DeferredActionId { get; set; }
    public string DeferReason { get; set; } = null!;
    public DateOnly DeferUntil { get; set; }
    public short ActionTypeId { get; set; }
    public string? ActionTypeNameAr { get; set; }
    public string? ActionDescription { get; set; }
    public string FollowupNote { get; set; } = null!;
    public string Status { get; set; } = null!;
}
