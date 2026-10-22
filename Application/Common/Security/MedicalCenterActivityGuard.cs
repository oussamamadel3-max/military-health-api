using Application.Exceptions;
using Domain.Entities.Core;

namespace Application.Common.Security;

/// <summary>
/// يمنع إنشاء إسناد أو معاملة تشغيلية جديدة على مركز غير فعّال، دون المساس بالسجلات التاريخية.
/// </summary>
public static class MedicalCenterActivityGuard
{
    public static void EnsureAcceptsNewRelations(MedicalCenter? center)
    {
        if (center is null)
            throw new MedicalCenterNotFoundException();

        if (!center.IsActive)
            throw new InactiveMedicalCenterException();
    }
}
