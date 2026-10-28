namespace Application.Dtos.ClinicWorkspace;

/// <summary>معاملات قائمة ومتابعة الحالات المؤجلة في العيادة (FR-042).</summary>
public class DeferredActionListQuery
{
    public Guid ClinicId { get; set; }
    public string? Status { get; set; }
    public short? ActionTypeId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public long? FileNumber { get; set; }
    public string? FullName { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>عنصر قائمة مؤجلات العيادة ضمن المركز النشط.</summary>
public class DeferredActionListItemDto
{
    public Guid DeferredActionId { get; set; }
    public Guid MemberId { get; set; }
    public long FileNumber { get; set; }
    public string FullName { get; set; } = null!;
    public Guid ClinicId { get; set; }
    public string? ClinicNameAr { get; set; }
    public string DeferReason { get; set; } = null!;
    public short ActionTypeId { get; set; }
    public string? ActionTypeNameAr { get; set; }
    public string? ActionDescription { get; set; }
    public string FollowupNote { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateOnly? DeferUntil { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public string? RequestedByUserName { get; set; }
    public string? CompletedByUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>مؤشر متابعة محسوب: مطلوب وما زال DeferUntil قبل اليوم.</summary>
    public bool IsOverdue { get; set; }
}

/// <summary>نتيجة تعليم الإجراء المطلوب مكتملاً دون تغيير تقييم العيادة.</summary>
public class DeferredActionCompletedDto
{
    public Guid DeferredActionId { get; set; }
    public Guid MemberId { get; set; }
    public Guid ClinicId { get; set; }
    public string Status { get; set; } = null!;
    public string CompletedByUserId { get; set; } = null!;
    public DateTimeOffset CompletedAt { get; set; }
}
