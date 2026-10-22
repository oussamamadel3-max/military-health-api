using Application.Exceptions;
using Domain.Entities.Core;

namespace Application.Common.Security;

/// <summary>
/// يمنع إسناد طبيب أو معاملة تعتمد على جاهزية العيادة في المركز عندما تكون علاقة CenterClinic غير فعّالة.
/// </summary>
public static class CenterClinicActivityGuard
{
    public static void EnsureAcceptsDoctorAssignment(CenterClinic? link)
    {
        if (link is null)
            throw new CenterClinicNotFoundException();

        if (!link.IsActive)
            throw new InactiveCenterClinicException();
    }
}
