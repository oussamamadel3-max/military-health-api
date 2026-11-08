namespace Application.Dtos.AuthDtos;

/// <summary>عنصر في قسم آخر العمليات الإدارية على حساب مستخدم.</summary>
public class UserAdminOperationDto
{
    public long EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorDisplayName { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Result { get; set; } = "SUCCESS";
}
