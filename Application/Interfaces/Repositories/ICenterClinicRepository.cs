using Domain.Entities.Core;

namespace Application.Interfaces.Repositories;

public interface ICenterClinicRepository
{
    IQueryable<CenterClinic> Query(bool asNoTracking = true);

    Task<CenterClinic?> GetByIdAsync(Guid centerClinicId, bool asNoTracking = false, CancellationToken cancellationToken = default);

    Task<CenterClinic?> GetByCenterAndClinicAsync(
        Guid centerId,
        Guid clinicId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    void Add(CenterClinic link);
}
