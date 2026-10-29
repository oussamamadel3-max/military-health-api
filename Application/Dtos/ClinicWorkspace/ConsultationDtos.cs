namespace Application.Dtos.ClinicWorkspace;

/// <summary>طلب إنشاء استشارة تخصصية مرتبطة بالمنتسب وعيادة الطبيب (FR-046).</summary>
public class CreateConsultationRequest
{
    /// <summary>عيادة الطبيب الطالب ضمن المركز النشط؛ لا يُقبل مركز من العميل.</summary>
    public Guid ClinicId { get; set; }

    /// <summary>نوع الاستشارة من القائمة المرجعية consultation-types.</summary>
    public short ConsultationTypeId { get; set; }

    /// <summary>سبب الإحالة (إلزامي).</summary>
    public string ReferralReason { get; set; } = null!;

    /// <summary>نتيجة الاستشارة عند التوفر؛ غيابها يُبقي الحالة PENDING.</summary>
    public string? ResultText { get; set; }

    /// <summary>معرف المستخدم الاستشاري عند توفره.</summary>
    public string? ConsultantUserId { get; set; }

    /// <summary>اسم الطبيب الاستشاري عند توفره دون حساب مستخدم.</summary>
    public string? ConsultantName { get; set; }
}

/// <summary>طلب تسجيل أو تحديث نتيجة استشارة معلّقة (FR-046 / A3).</summary>
public class UpdateConsultationResultRequest
{
    /// <summary>نص نتيجة الاستشارة (إلزامي لإكمال الحالة).</summary>
    public string ResultText { get; set; } = null!;

    /// <summary>معرف المستخدم الاستشاري عند توفره.</summary>
    public string? ConsultantUserId { get; set; }

    /// <summary>اسم الطبيب الاستشاري عند توفره.</summary>
    public string? ConsultantName { get; set; }
}

/// <summary>استجابة استشارة تخصصية مع بيانات العرض الأساسية.</summary>
public class ConsultationDto
{
    public Guid ConsultationId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string? ClinicNameAr { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public string? RequestedByUserName { get; set; }
    public string? ConsultantUserId { get; set; }
    public string? ConsultantName { get; set; }
    public short ConsultationTypeId { get; set; }
    public string? ConsultationTypeNameAr { get; set; }
    public string ReferralReason { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? ResultText { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResultedAt { get; set; }
}
