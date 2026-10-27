namespace Application.Dtos.ClinicWorkspace;

/// <summary>طلب حفظ فحص الباطنة مع تقييم العيادة في عملية واحدة (FR-035 / شريحة FR-039).</summary>
public class SaveInternalExamRequest
{
    /// <summary>عيادة الباطنة المسندة ضمن المركز النشط.</summary>
    public Guid ClinicId { get; set; }

    /// <summary>نتيجة فحص الجهاز القلبي.</summary>
    public string Cardiovascular { get; set; } = null!;

    /// <summary>نتيجة فحص الجهاز التنفسي.</summary>
    public string Respiratory { get; set; } = null!;

    /// <summary>نتيجة فحص الجهاز الهضمي.</summary>
    public string Digestive { get; set; } = null!;

    /// <summary>نتيجة فحص الغدد الصماء.</summary>
    public string Endocrine { get; set; } = null!;

    /// <summary>نتيجة فحص الجهاز العصبي.</summary>
    public string Neurological { get; set; } = null!;

    /// <summary>نتيجة فحص الدم.</summary>
    public string Hematology { get; set; } = null!;

    /// <summary>نتيجة فحص المفاصل.</summary>
    public string Joints { get; set; } = null!;

    /// <summary>نتيجة فحص الكلية.</summary>
    public string Renal { get; set; } = null!;

    /// <summary>نتيجة فحص الجلد.</summary>
    public string Skin { get; set; } = null!;

    /// <summary>نتيجة التقييم المرجعية: 1 مقبول، 2 مرفوض، 3 مؤجل.</summary>
    public short ResultStatusId { get; set; }

    /// <summary>سبب الرفض إلزامي عند مرفوض.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>ملاحظات سريرية اختيارية على التقييم.</summary>
    public string? ClinicalNotes { get; set; }

    /// <summary>سبب التأجيل إلزامي عند مؤجل.</summary>
    public string? DeferReason { get; set; }

    /// <summary>نوع الإجراء المطلوب عند مؤجل.</summary>
    public short? ActionTypeId { get; set; }

    /// <summary>ملاحظة المتابعة الإلزامية عند مؤجل.</summary>
    public string? FollowupNote { get; set; }

    /// <summary>توصيف نصي عند أنواع إجراء تتطلبه.</summary>
    public string? ActionDescription { get; set; }

    /// <summary>عيادة مستهدفة عند أنواع إجراء تتطلبها.</summary>
    public Guid? TargetClinicId { get; set; }

    /// <summary>تاريخ متابعة اختياري لتأجيل العيادة.</summary>
    public DateOnly? DeferUntil { get; set; }
}

/// <summary>نتيجة حفظ أو عرض فحص الباطنة مع التقييم المرتبط.</summary>
public class InternalExamDto
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public DateTimeOffset ExaminedAt { get; set; }
    public int ExamVersionNo { get; set; }
    public string? Cardiovascular { get; set; }
    public string? Respiratory { get; set; }
    public string? Digestive { get; set; }
    public string? Endocrine { get; set; }
    public string? Neurological { get; set; }
    public string? Hematology { get; set; }
    public string? Joints { get; set; }
    public string? Renal { get; set; }
    public string? Skin { get; set; }
    public Guid? AssessmentId { get; set; }
    public Guid? DeferredActionId { get; set; }
    public short? ResultStatusId { get; set; }
    public string? RejectionReason { get; set; }
    public string? ClinicalNotes { get; set; }
    public string? AssessedByUserId { get; set; }
    public DateTimeOffset? AssessedAt { get; set; }
}
