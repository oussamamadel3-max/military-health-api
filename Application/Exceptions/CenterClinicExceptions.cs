using Domain.Exceptions;

namespace Application.Exceptions;

public class CenterClinicNotFoundException : BusinessException
{
    public CenterClinicNotFoundException(string message = "ربط العيادة بالمركز غير موجود.")
        : base(message, "CENTER_CLINIC_NOT_FOUND", 404)
    {
    }
}

public class CenterClinicAlreadyExistsException : BusinessException
{
    public CenterClinicAlreadyExistsException()
        : base("العيادة مرتبطة بهذا المركز مسبقاً.", "CENTER_CLINIC_ALREADY_EXISTS", 400)
    {
    }
}

public class InactiveClinicException : BusinessException
{
    public InactiveClinicException(string message = "لا يمكن ربط عيادة معطّلة بمركز.")
        : base(message, "CLINIC_INACTIVE", 400)
    {
    }
}

public class ClinicNotFoundException : BusinessException
{
    public ClinicNotFoundException(string message = "العيادة غير موجودة.")
        : base(message, "CLINIC_NOT_FOUND", 404)
    {
    }
}

public class InactiveCenterClinicException : BusinessException
{
    public InactiveCenterClinicException(string message = "لا يمكن إسناد طبيب إلى عيادة غير جاهزة في هذا المركز.")
        : base(message, "CENTER_CLINIC_INACTIVE", 400)
    {
    }
}

public class CenterClinicPhysicalDeleteNotAllowedException : BusinessException
{
    public CenterClinicPhysicalDeleteNotAllowedException()
        : base(
            "لا يُسمح بالحذف النهائي لربط العيادة بالمركز. استخدم التعطيل.",
            "CENTER_CLINIC_DELETE_FORBIDDEN",
            400)
    {
    }
}

public class InvalidCenterClinicOperationException : BusinessException
{
    public InvalidCenterClinicOperationException(string message)
        : base(message, "CENTER_CLINIC_INVALID_OPERATION", 400)
    {
    }
}
