using Domain.Exceptions;

namespace Application.Exceptions;

/// <summary>يُرمى عند معايير بحث/تصفية غير صالحة لقائمة متابعة المشرف (FR-057).</summary>
public class InvalidSupervisorFollowUpDataException : BusinessException
{
    public InvalidSupervisorFollowUpDataException(string message, object? meta = null)
        : base(message, "SUPERVISOR_FOLLOW_UP_INVALID_QUERY", 400, meta)
    {
    }
}
