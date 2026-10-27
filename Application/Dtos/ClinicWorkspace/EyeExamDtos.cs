namespace Application.Dtos.ClinicWorkspace;

/// <summary>ثوابت صيغة إدخال الانكسار المعتمدة في المخطط (FRACTION / DECIMAL).</summary>
public static class EyeRefractionInputFormats
{
    public const string Fraction = "FRACTION";
    public const string Decimal = "DECIMAL";
}

/// <summary>ثوابت جانب العين المعتمدة في المخطط (RIGHT / LEFT).</summary>
public static class EyeSides
{
    public const string Right = "RIGHT";
    public const string Left = "LEFT";
}

/// <summary>صف إدخال انكسار ضمن طلب حفظ فحص العيون (FR-033 / FR-034).</summary>
public class EyeRefractionInputDto
{
    /// <summary>معرّف صف موجود عند التعديل؛ فارغ للإدراج الجديد.</summary>
    public Guid? RefractionDetailId { get; set; }

    /// <summary>جانب العين: RIGHT أو LEFT.</summary>
    public string EyeSide { get; set; } = null!;

    /// <summary>نوع الانكسار كنص حر معتمد من الواجهة (لا قاموس مغلق حالياً).</summary>
    public string RefractionType { get; set; } = null!;

    /// <summary>صيغة الإدخال: FRACTION أو DECIMAL.</summary>
    public string InputFormat { get; set; } = null!;

    /// <summary>البسط عند الصيغة الكسرية.</summary>
    public decimal? Numerator { get; set; }

    /// <summary>المقام عند الصيغة الكسرية (يجب أن يكون &gt; 0).</summary>
    public decimal? Denominator { get; set; }

    /// <summary>القيمة العشرية عند الصيغة العشرية.</summary>
    public decimal? DecimalInput { get; set; }

    /// <summary>هل هذا أسوأ انكسار لهذا الجانب (وفي حدود نوع الانكسار).</summary>
    public bool IsWorst { get; set; }
}

/// <summary>طلب حفظ فحص العيون مع الانكسار وتقييم العيادة في عملية واحدة.</summary>
public class SaveEyeExamRequest
{
    /// <summary>عيادة العيون المسندة ضمن المركز النشط.</summary>
    public Guid ClinicId { get; set; }

    /// <summary>القدرة البصرية للعين اليمنى.</summary>
    public string VisualAcuityRight { get; set; } = null!;

    /// <summary>القدرة البصرية للعين اليسرى.</summary>
    public string VisualAcuityLeft { get; set; } = null!;

    /// <summary>نتيجة اختبار الألوان.</summary>
    public string ColorTestResult { get; set; } = null!;

    /// <summary>أمراض عينية أخرى اختيارية.</summary>
    public string? OtherEyeDiseases { get; set; }

    /// <summary>قيم الانكسار (صفر أو أكثر؛ لا يُحذف صف محفوظ عند التعديل).</summary>
    public List<EyeRefractionInputDto> Refractions { get; set; } = new();

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

/// <summary>صف انكسار معروض كما أُدخل مع القيمة المحسوبة.</summary>
public class EyeRefractionDetailDto
{
    public Guid RefractionDetailId { get; set; }
    public string EyeSide { get; set; } = null!;
    public string RefractionType { get; set; } = null!;
    public string InputFormat { get; set; } = null!;
    public decimal? Numerator { get; set; }
    public decimal? Denominator { get; set; }
    public decimal? DecimalInput { get; set; }
    public decimal? CalculatedValue { get; set; }
    public bool IsWorst { get; set; }
}

/// <summary>نتيجة حفظ أو عرض فحص العيون مع التقييم المرتبط.</summary>
public class EyeExamDto
{
    public Guid ExamId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string ExaminerUserId { get; set; } = null!;
    public DateTimeOffset ExaminedAt { get; set; }
    public int ExamVersionNo { get; set; }
    public string? VisualAcuityRight { get; set; }
    public string? VisualAcuityLeft { get; set; }
    public string? ColorTestResult { get; set; }
    public string? OtherEyeDiseases { get; set; }
    public IReadOnlyList<EyeRefractionDetailDto> Refractions { get; set; } = Array.Empty<EyeRefractionDetailDto>();
    public Guid? AssessmentId { get; set; }
    public Guid? DeferredActionId { get; set; }
    public short? ResultStatusId { get; set; }
    public string? RejectionReason { get; set; }
    public string? ClinicalNotes { get; set; }
    public string? AssessedByUserId { get; set; }
    public DateTimeOffset? AssessedAt { get; set; }
}
