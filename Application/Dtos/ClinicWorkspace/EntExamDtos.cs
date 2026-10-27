namespace Application.Dtos.ClinicWorkspace;

/// <summary>طلب حفظ فحص الأذنية مع تقييم العيادة في عملية واحدة (FR-038 / شريحة FR-039).</summary>
public class SaveEntExamRequest
{
    /// <summary>عيادة الأذنية المسندة ضمن المركز النشط.</summary>
    public Guid ClinicId { get; set; }

    /// <summary>نتيجة فحص الأذن اليمنى.</summary>
    public string RightEar { get; set; } = null!;

    /// <summary>نتيجة فحص الأذن اليسرى.</summary>
    public string LeftEar { get; set; } = null!;

    /// <summary>نتيجة فحص غشاء الطبل.</summary>
    public string TympanicMembrane { get; set; } = null!;

    /// <summary>نتيجة فحص السمع.</summary>
    public string HearingTest { get; set; } = null!;

    /// <summary>نتيجة اختبار الهمس.</summary>
    public string WhisperTest { get; set; } = null!;

    /// <summary>نتيجة فحص الأنف.</summary>
    public string NoseExam { get; set; } = null!;

    /// <summary>نتيجة فحص الفم.</summary>
    public string MouthExam { get; set; } = null!;

    /// <summary>أمراض أذنية أخرى اختيارية.</summary>
    public string? OtherEntDiseases { get; set; }

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

/// <summary>نتيجة حفظ أو عرض فحص الأذنية مع التقييم المرتبط.</summary>
public class EntExamDto
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public DateTimeOffset ExaminedAt { get; set; }
    public int ExamVersionNo { get; set; }
    public string? RightEar { get; set; }
    public string? LeftEar { get; set; }
    public string? TympanicMembrane { get; set; }
    public string? HearingTest { get; set; }
    public string? WhisperTest { get; set; }
    public string? NoseExam { get; set; }
    public string? MouthExam { get; set; }
    public string? OtherEntDiseases { get; set; }
    public Guid? AssessmentId { get; set; }
    public Guid? DeferredActionId { get; set; }
    public short? ResultStatusId { get; set; }
    public string? RejectionReason { get; set; }
    public string? ClinicalNotes { get; set; }
    public string? AssessedByUserId { get; set; }
    public DateTimeOffset? AssessedAt { get; set; }
}
