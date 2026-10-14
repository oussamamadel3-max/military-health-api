using Domain.Exceptions;

namespace Application.Exceptions;

public class MedicalCenterNotFoundException : BusinessException
{
    public MedicalCenterNotFoundException(string message = "المركز الصحي غير موجود.")
        : base(message, "MEDICAL_CENTER_NOT_FOUND", 404)
    {
    }
}

public class MedicalCenterCodeAlreadyExistsException : BusinessException
{
    public MedicalCenterCodeAlreadyExistsException(string code = "")
        : base(
            string.IsNullOrWhiteSpace(code)
                ? "رمز المركز مستخدم مسبقاً."
                : $"رمز المركز '{code}' مستخدم مسبقاً.",
            "MEDICAL_CENTER_CODE_EXISTS",
            400)
    {
    }
}

public class InvalidMedicalCenterDataException : BusinessException
{
    public InvalidMedicalCenterDataException(string message)
        : base(message, "MEDICAL_CENTER_INVALID_DATA", 400)
    {
    }
}

public class InactiveMedicalCenterException : BusinessException
{
    public InactiveMedicalCenterException(string message = "لا يمكن إنشاء إسناد أو معاملة جديدة لمركز صحي معطّل.")
        : base(message, "MEDICAL_CENTER_INACTIVE", 400)
    {
    }
}

public class MedicalCenterPhysicalDeleteNotAllowedException : BusinessException
{
    public MedicalCenterPhysicalDeleteNotAllowedException()
        : base(
            "لا يُسمح بالحذف النهائي للمركز الصحي. استخدم التعطيل مع تسجيل السبب.",
            "MEDICAL_CENTER_DELETE_FORBIDDEN",
            400)
    {
    }
}
