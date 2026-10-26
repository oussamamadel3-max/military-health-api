using Domain.Exceptions;

namespace Application.Exceptions;

/// <summary>يُرمى عندما لا يكون الطبيب مسنداً بفعالية إلى العيادة المطلوبة في المركز النشط.</summary>
public class DoctorClinicNotAssignedException : BusinessException
{
    public DoctorClinicNotAssignedException()
        : base(
            "لست مسنداً إلى هذه العيادة في المركز النشط.",
            "DOCTOR_CLINIC_NOT_ASSIGNED",
            403)
    {
    }
}

/// <summary>يُرمى عندما يكون clinicId مفقوداً أو غير صالح لعمليات مساحة عمل العيادة.</summary>
public class InvalidClinicWorkspaceDataException : BusinessException
{
    public InvalidClinicWorkspaceDataException(string message)
        : base(message, "CLINIC_WORKSPACE_INVALID_DATA", 400)
    {
    }
}
