using Domain.Exceptions;

namespace Application.Exceptions;

/// <summary>يُرمى عندما لا يوجد سجل أرشيف ضمن النطاق المخوّل.</summary>
public class ArchiveNotFoundException : BusinessException
{
    public ArchiveNotFoundException(string message = "سجل الأرشيف غير موجود.")
        : base(message, "ARCHIVE_NOT_FOUND", 404)
    {
    }
}

/// <summary>يُرمى عند فلاتر بحث أرشيف غير صالحة.</summary>
public class InvalidArchiveFilterException : BusinessException
{
    public InvalidArchiveFilterException(string message)
        : base(message, "ARCHIVE_INVALID_FILTER", 400)
    {
    }
}
