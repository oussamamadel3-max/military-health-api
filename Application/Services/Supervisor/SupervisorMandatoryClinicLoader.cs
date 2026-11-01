using Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Supervisor;

/// <summary>
/// تحميل العيادات الإلزامية للقرار النهائي: نشطة وCountsForFinal ومرتبطة بـ CenterClinic فعّال.
/// مشترك بين ملف المشرف وإصدار القرار.
/// </summary>
public static class SupervisorMandatoryClinicLoader
{
    /// <summary>عيادة إلزامية ضمن جاهزية المركز لإصدار القرار.</summary>
    public sealed record ClinicInfo(Guid ClinicId, string Code, string NameAr, short SortOrder);

    /// <summary>يعيد العيادات الإلزامية للمركز مرتبة حسب SortOrder.</summary>
    public static async Task<IReadOnlyList<ClinicInfo>> LoadAsync(
        Guid centerId,
        ICenterClinicRepository centerClinics,
        IReferenceDataRepository referenceData,
        CancellationToken cancellationToken = default)
    {
        var linkedClinicIds = await centerClinics.Query(asNoTracking: true)
            .Where(l => l.CenterId == centerId && l.IsActive)
            .Select(l => l.ClinicId)
            .ToListAsync(cancellationToken);

        if (linkedClinicIds.Count == 0)
            return Array.Empty<ClinicInfo>();

        return await referenceData.Clinics(asNoTracking: true)
            .Where(c => linkedClinicIds.Contains(c.ClinicId)
                && c.IsActive
                && c.CountsForFinal)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.NameAr)
            .Select(c => new ClinicInfo(c.ClinicId, c.Code, c.NameAr, c.SortOrder))
            .ToListAsync(cancellationToken);
    }
}
