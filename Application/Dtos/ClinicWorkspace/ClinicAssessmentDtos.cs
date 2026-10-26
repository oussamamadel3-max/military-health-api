namespace Application.Dtos.ClinicWorkspace;

/// <summary>طلب حفظ تقييم العيادة مع هيكل الفحص الأساسي (FR-040 / FR-041).</summary>
public class SaveClinicAssessmentRequest
{
    /// <summary>عيادة الطبيب المسندة ضمن المركز النشط.</summary>
    public Guid ClinicId { get; set; }

    /// <summary>نتيجة التقييم المرجعية: 1 مقبول، 2 مرفوض، 3 مؤجل.</summary>
    public short ResultStatusId { get; set; }

    /// <summary>سبب الرفض إلزامي عند النتيجة مرفوض؛ يجب أن يكون فارغاً لغير المرفوض.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>ملاحظات سريرية اختيارية على التقييم.</summary>
    public string? ClinicalNotes { get; set; }

    /// <summary>سبب التأجيل إلزامي عند مؤجل (يُحفظ على DeferredAction).</summary>
    public string? DeferReason { get; set; }

    /// <summary>نوع الإجراء المطلوب عند مؤجل.</summary>
    public short? ActionTypeId { get; set; }

    /// <summary>ملاحظة المتابعة الإلزامية عند مؤجل.</summary>
    public string? FollowupNote { get; set; }

    /// <summary>توصيف نصي عند أنواع إجراء تتطلبه.</summary>
    public string? ActionDescription { get; set; }

    /// <summary>عيادة مستهدفة عند أنواع إجراء تتطلبها.</summary>
    public Guid? TargetClinicId { get; set; }

    /// <summary>تاريخ متابعة اختياري لتأجيل العيادة (إلزامي فقط للقرار النهائي خارج هذا المسار).</summary>
    public DateOnly? DeferUntil { get; set; }
}

/// <summary>نتيجة حفظ تقييم العيادة والفحص الأساسي المرتبط.</summary>
public class ClinicAssessmentSavedDto
{
    public Guid ExamId { get; set; }
    public Guid AssessmentId { get; set; }
    public Guid? DeferredActionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public short ResultStatusId { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public string AssessedByUserId { get; set; } = null!;
    public DateTimeOffset ExaminedAt { get; set; }
    public DateTimeOffset AssessedAt { get; set; }
    public int ExamVersionNo { get; set; }
}
