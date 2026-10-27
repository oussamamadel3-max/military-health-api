namespace Application.Dtos.ClinicWorkspace;

/// <summary>طلب حفظ فحص العظمية مع تقييم العيادة في عملية واحدة (FR-037 / شريحة FR-039).</summary>
public class SaveOrthopedicExamRequest
{
    /// <summary>العيادة العظمية المسندة ضمن المركز النشط.</summary>
    public Guid ClinicId { get; set; }

    /// <summary>تقييم الجهاز العضلي الهيكلي (الأطراف والعمود الفقري والمفاصل).</summary>
    public string Musculoskeletal { get; set; } = null!;

    /// <summary>تقييم الجراحة العصبية المرتبط بالحركة والإحساس.</summary>
    public string Neurosurgery { get; set; } = null!;

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

/// <summary>نتيجة حفظ أو عرض فحص العظمية مع التقييم المرتبط.</summary>
public class OrthopedicExamDto
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public DateTimeOffset ExaminedAt { get; set; }
    public int ExamVersionNo { get; set; }
    public string? Musculoskeletal { get; set; }
    public string? Neurosurgery { get; set; }
    public Guid? AssessmentId { get; set; }
    public Guid? DeferredActionId { get; set; }
    public short? ResultStatusId { get; set; }
    public string? RejectionReason { get; set; }
    public string? ClinicalNotes { get; set; }
    public string? AssessedByUserId { get; set; }
    public DateTimeOffset? AssessedAt { get; set; }
}
