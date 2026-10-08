using Application.Common.Results;
using Domain.Entities.Core;

namespace Application.Interfaces.Repositories;

public interface IOrgUnitTypeRepository : IRepository<OrgUnitType, Guid>
{
    Task<PaginatedList<OrgUnitType>> GetPagedAsync(int page, int size, string? query, bool? isActive, CancellationToken ct = default);
    Task<IReadOnlyList<OrgUnitType>> GetActiveAsync(CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
}
