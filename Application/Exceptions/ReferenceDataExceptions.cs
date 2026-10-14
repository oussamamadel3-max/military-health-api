using Domain.Exceptions;

namespace Application.Exceptions;

public class ReferenceDataNotFoundException : BusinessException
{
    public ReferenceDataNotFoundException(string message = "القيمة المرجعية غير موجودة.")
        : base(message, "REFERENCE_DATA_NOT_FOUND", 404)
    {
    }
}

public class InvalidReferenceDataException : BusinessException
{
    public InvalidReferenceDataException(string message)
        : base(message, "REFERENCE_DATA_INVALID", 400)
    {
    }
}

public class ReferenceDataDuplicateException : BusinessException
{
    public ReferenceDataDuplicateException(string message)
        : base(message, "REFERENCE_DATA_DUPLICATE", 400)
    {
    }
}

public class ReferenceDataLockedException : BusinessException
{
    public ReferenceDataLockedException(string message = "هذه القيمة المرجعية أساسية ومقفلة ولا يجوز تعديلها أو حذفها.")
        : base(message, "REFERENCE_DATA_LOCKED", 400)
    {
    }
}

public class ReferenceDataDeleteNotAllowedException : BusinessException
{
    public ReferenceDataDeleteNotAllowedException()
        : base(
            "لا يُسمح بالحذف النهائي للقيم المرجعية. استخدم التعطيل مع الإبقاء على السجلات التاريخية.",
            "REFERENCE_DATA_DELETE_FORBIDDEN",
            400)
    {
    }
}
