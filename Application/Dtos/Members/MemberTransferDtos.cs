namespace Application.Dtos.Members;

/// <summary>طلب نقل ملف منتسب إلى مركز صحي فعّال آخر (FR-073).</summary>
public class TransferMemberRequest
{
    /// <summary>معرّف المركز الهدف؛ يُرفض إن طابق المركز الحالي أو كان غير فعّال.</summary>
    public Guid ToCenterId { get; set; }

    /// <summary>سبب النقل الإلزامي.</summary>
    public string TransferReason { get; set; } = null!;

    /// <summary>مراجعة بيانات متوقعة اختيارية لمنع النقل المتزامن عند تعارض المراجعة.</summary>
    public long? ExpectedDataRevision { get; set; }
}

/// <summary>نتيجة تنفيذ النقل مع بقاء رقم الملف وتحديث الدور في المركز الهدف.</summary>
public class MemberTransferResultDto
{
    public Guid TransferId { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public Guid FromCenterId { get; set; }
    public string? FromCenterNameAr { get; set; }
    public Guid ToCenterId { get; set; }
    public string? ToCenterNameAr { get; set; }
    public string TransferReason { get; set; } = null!;
    public string ExecutedByUserId { get; set; } = null!;
    public DateTimeOffset ExecutedAt { get; set; }
    public DateOnly QueueDate { get; set; }
    public int QueueNumber { get; set; }
    public long DataRevision { get; set; }
    public string ExportStatusAtTransfer { get; set; } = null!;
    public IReadOnlyList<string> NoticesAr { get; set; } = Array.Empty<string>();
}

/// <summary>تاريخ نقل الملف مع تمييز المركز الحالي (FR-074).</summary>
public class MemberTransferHistoryDto
{
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public Guid CurrentMedicalCenterId { get; set; }
    public string? CurrentMedicalCenterNameAr { get; set; }
    public bool HasTransfers { get; set; }
    public string? EmptyMessageAr { get; set; }
    public IReadOnlyList<MemberTransferHistoryItemDto> Items { get; set; }
        = Array.Empty<MemberTransferHistoryItemDto>();
}

/// <summary>عنصر سجل نقل واحد مرتب زمنياً.</summary>
public class MemberTransferHistoryItemDto
{
    public Guid TransferId { get; set; }
    public Guid FromCenterId { get; set; }
    public string? FromCenterNameAr { get; set; }
    public Guid ToCenterId { get; set; }
    public string? ToCenterNameAr { get; set; }
    public string TransferReason { get; set; } = null!;
    public string ExecutedByUserId { get; set; } = null!;
    public string? ExecutedByUserName { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
}
