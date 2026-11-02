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

/// <summary>يُرمى عند محاولة تصدير قائمة متابعة بلا نتائج مطابقة للتصفية.</summary>
public class SupervisorFollowUpExportEmptyException : BusinessException
{
    public SupervisorFollowUpExportEmptyException()
        : base("لا توجد نتائج ضمن التصفية الحالية.", "SUPERVISOR_FOLLOW_UP_EXPORT_EMPTY", 400)
    {
    }
}

/// <summary>يُرمى عند تجاوز حد صفوف تصدير قائمة متابعة المشرف.</summary>
public class SupervisorFollowUpExportTooLargeException : BusinessException
{
    public SupervisorFollowUpExportTooLargeException(int totalCount, int maxRows)
        : base(
            $"عدد السجلات ({totalCount}) يتجاوز حد التصدير ({maxRows}). ضيّق التصفية.",
            "SUPERVISOR_FOLLOW_UP_EXPORT_TOO_LARGE",
            400)
    {
    }
}
