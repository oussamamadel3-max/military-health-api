namespace Application.Dtos.ClinicWorkspace;

/// <summary>طلب إنشاء استقصاء مخبري أو شعاعي مرتبط بالمنتسب وعيادة الطبيب (FR-047).</summary>
public class CreateInvestigationRequest
{
    /// <summary>عيادة الطبيب الطالب ضمن المركز النشط؛ لا يُقبل مركز من العميل.</summary>
    public Guid ClinicId { get; set; }

    /// <summary>نوع الاستقصاء من القائمة المرجعية investigation-types.</summary>
    public short InvestigationTypeId { get; set; }

    /// <summary>اسم الفحص أو الاختبار (إلزامي).</summary>
    public string TestName { get; set; } = null!;

    /// <summary>سبب الطلب (إلزامي على مستوى التطبيق).</summary>
    public string RequestReason { get; set; } = null!;

    /// <summary>نتيجة الاستقصاء عند التوفر؛ غيابها يُبقي الحالة PENDING.</summary>
    public string? ResultText { get; set; }

    /// <summary>ربط اختياري بإجراء مؤجّل عند الإنشاء من حالة مؤجلة؛ لا يُغلق الإجراء تلقائياً.</summary>
    public Guid? DeferredActionId { get; set; }
}

/// <summary>طلب تسجيل نتيجة استقصاء معلّق (FR-047 / A4).</summary>
public class UpdateInvestigationResultRequest
{
    /// <summary>نص نتيجة الاستقصاء (إلزامي لإكمال الحالة).</summary>
    public string ResultText { get; set; } = null!;
}

/// <summary>استجابة استقصاء مخبري/شعاعي مع بيانات العرض الأساسية.</summary>
public class InvestigationDto
{
    public Guid InvestigationId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string? ClinicNameAr { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public string? RequestedByUserName { get; set; }
    public short InvestigationTypeId { get; set; }
    public string? InvestigationTypeNameAr { get; set; }
    public string TestName { get; set; } = null!;
    public string? RequestReason { get; set; }
    public string Status { get; set; } = null!;
    public string? ResultText { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResultedAt { get; set; }
    public Guid? DeferredActionId { get; set; }
    public string? DeferredActionStatus { get; set; }
}
