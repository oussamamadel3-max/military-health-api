using Application.Exceptions;
using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Security;

/// <summary>
/// يتحقق من أن الطبيب مسند إلى العيادة داخل المركز قبل أي كتابة فحص.
/// إسناد الدور وحده لا يكفي، وسحب الإسناد يُعيد الرفض في الطلب التالي.
/// </summary>
public static class DoctorClinicAccess
{
    public static async Task<CenterClinic> RequireActiveAssignmentAsync(
        IUserRepository users,
        ICenterClinicRepository clinics,
        Guid centerId,
        Guid clinicId,
        string userId,
        CancellationToken cancellationToken)
    {
        var assigned = await users.QueryUserClinicAssignments()
            .AnyAsync(a => a.UserId == userId
                && a.CenterId == centerId
                && a.ClinicId == clinicId
                && a.IsActive
                && a.RevokedAt == null, cancellationToken);

        if (!assigned)
            throw new DoctorClinicNotAssignedException();

        var link = await clinics.GetByCenterAndClinicAsync(
            centerId,
            clinicId,
            asNoTracking: true,
            cancellationToken);

        if (link is null || !link.IsActive || link.Clinic is null)
            throw new DoctorClinicNotAssignedException();

        return link;
    }
}
