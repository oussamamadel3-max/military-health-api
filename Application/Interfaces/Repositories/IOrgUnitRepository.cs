using Application.Common.Results;
using Domain.Entities.Core;

namespace Application.Interfaces.Repositories;

public interface IOrgUnitRepository : IRepository<OrgUnit, Guid>
{
    Task<OrgUnit?> GetDetailedAsync(Guid id, CancellationToken ct = default);
    Task<PaginatedList<OrgUnit>> GetPagedAsync(int page, int size, string? query, string? status, CancellationToken ct = default);
    Task<IReadOnlyList<OrgUnit>> GetDropdownAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OrgUnit>> GetTreeUnitsAsync(string? status, CancellationToken ct = default);
    Task<IReadOnlyList<OrgUnit>> GetChildrenAsync(Guid parentId, CancellationToken ct = default);
    Task<IReadOnlyList<OrgUnit>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, bool excludeArchived = false, CancellationToken ct = default);
    Task<bool> AnyAsync(CancellationToken ct = default);
    Task<bool> HasActiveChildrenAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId, CancellationToken ct = default);
    Task<bool> SiblingNameExistsAsync(Guid? parentId, string name, Guid? excludeId, CancellationToken ct = default);
    Task<IReadOnlyList<(Guid Id, Guid? ParentId)>> GetNonArchivedParentLinksAsync(CancellationToken ct = default);
}
