using Domain.Entities.Core;

namespace Application.Interfaces.Repositories;

public interface IMedicalCenterRepository
{
    IQueryable<MedicalCenter> Query(bool asNoTracking = true);
    Task<MedicalCenter?> GetByIdAsync(Guid centerId, bool asNoTracking = false, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludingCenterId = null, CancellationToken cancellationToken = default);
    void Add(MedicalCenter center);
    Task<int> CountActiveUsersAsync(Guid centerId, CancellationToken cancellationToken = default);
    Task<int> CountActiveClinicsAsync(Guid centerId, CancellationToken cancellationToken = default);
}
