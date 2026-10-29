namespace Application.Dtos.ClinicWorkspace;

/// <summary>
/// عقد عرض مجمّع لاستشارات واستقصاءات المنتسب مع المرفقات (FR-050).
/// قابل لإعادة الاستخدام لاحقاً من شاشة المشرف دون نظام موازٍ.
/// </summary>
public class MemberMedicalRequestsDto
{
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }

    /// <summary>true عند عدم وجود استشارات أو استقصاءات ضمن نطاق المركز.</summary>
    public bool IsEmpty { get; set; }

    /// <summary>رسالة Empty State بالعربية عند IsEmpty؛ null عند وجود سجلات.</summary>
    public string? EmptyMessageAr { get; set; }

    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int CompletedCount { get; set; }

    /// <summary>true عندما تُضمَّن بيانات المرفقات بصلاحية AttachmentsRead.</summary>
    public bool AttachmentsIncluded { get; set; }

    /// <summary>سجلات موحّدة مصنّفة بحقول العيادة/التخصص/النوع/الحالة.</summary>
    public IReadOnlyList<MemberMedicalRequestItemDto> Items { get; set; } = Array.Empty<MemberMedicalRequestItemDto>();
}

/// <summary>سجل استشارة أو استقصاء ضمن العرض المجمّع للمنتسب.</summary>
public class MemberMedicalRequestItemDto
{
    /// <summary>Consultation أو Investigation.</summary>
    public string RequestKind { get; set; } = null!;

    public Guid RequestId { get; set; }
    public Guid ClinicId { get; set; }
    public string? ClinicNameAr { get; set; }

    /// <summary>في نموذج العيادات الخمس يطابق اسم العيادة التخصص المعروض.</summary>
    public string? SpecialtyNameAr { get; set; }

    public string? SpecialtyCode { get; set; }
    public short TypeId { get; set; }
    public string? TypeNameAr { get; set; }

    /// <summary>سبب الطلب (إحالة الاستشارة أو سبب الاستقصاء).</summary>
    public string? RequestReason { get; set; }

    /// <summary>اسم الفحص للاستقصاء فقط.</summary>
    public string? TestName { get; set; }

    public string Status { get; set; } = null!;
    public bool IsPending { get; set; }
    public string? ResultText { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public string? RequestedByUserName { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResultedAt { get; set; }

    public string? ConsultantUserId { get; set; }
    public string? ConsultantName { get; set; }
    public Guid? DeferredActionId { get; set; }

    public IReadOnlyList<MedicalRequestAttachmentDto> Attachments { get; set; }
        = Array.Empty<MedicalRequestAttachmentDto>();
}

/// <summary>
/// بيانات مرفق للعرض دون كشف مسار التخزين الداخلي.
/// التنزيل عبر المسار المحمي الحالي api/Attachments/download/{fileAssetId}.
/// </summary>
public class MedicalRequestAttachmentDto
{
    public Guid FileAssetId { get; set; }
    public string FileName { get; set; } = null!;
    public string? FileExtension { get; set; }
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>مسار API نسبي للتنزيل المحمي؛ ليس StorageKey.</summary>
    public string DownloadPath { get; set; } = null!;
}
