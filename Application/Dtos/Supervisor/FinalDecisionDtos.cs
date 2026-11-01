namespace Application.Dtos.Supervisor;

/// <summary>طلب إصدار القرار النهائي المعتمد (قبول/رفض فقط في MH-P6-020).</summary>
public class IssueFinalDecisionRequest
{
    /// <summary>1=مقبول، 2=مرفوض. التأجيل (3) خارج نطاق هذه البطاقة.</summary>
    public short ResultStatusId { get; set; }

    /// <summary>سبب الرفض إلزامي عندما ResultStatusId = 2؛ يجب أن يكون null/فارغاً عند القبول.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>مبرر الاختلاف عن تقييم عيادة أو أكثر؛ إلزامي عند التعارض.</summary>
    public string? DisagreementJustification { get; set; }

    /// <summary>
    /// مراجعة بيانات المنتسب المتوقعة للحماية من اعتماد متزامن.
    /// عند الإرسال وعدم التطابق يُرفض الطلب بـ 409.
    /// </summary>
    public long? ExpectedDataRevision { get; set; }
}

/// <summary>نتيجة إصدار القرار النهائي المعتمد.</summary>
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

    /// <summary>عدد سجلات اعتراض المشرف المُنشأة لهذا القرار.</summary>
    public int ObjectionCount { get; set; }

    /// <summary>
    /// نقطة ربط لأرشفة القرار (T-10 / BR-017). false في هذه البطاقة لأن الأرشفة خارج النطاق.
    /// </summary>
    public bool ArchiveCreated { get; set; }
}
