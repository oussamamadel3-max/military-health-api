using Application.Common.Results;
using Domain.Entities.Core;

namespace Application.Interfaces.Repositories;

public sealed record OrgScopeLink(Guid OrgUnitId, string ScopeMode);

public interface IUserOrgScopeRepository : IRepository<UserOrgScope, Guid>
{
    Task<UserOrgScope?> GetDetailedAsync(Guid id, CancellationToken ct = default);
    Task<PaginatedList<UserOrgScope>> GetPagedAsync(int page, int size, string? query, string? status, CancellationToken ct = default);
    Task<IReadOnlyList<UserOrgScope>> GetByUserIdAsync(string userId, bool activeOnly = false, CancellationToken ct = default);
    Task<IReadOnlyList<OrgScopeLink>> GetActiveScopesForPermissionAsync(string userId, Guid permissionId, CancellationToken ct = default);
    Task<bool> DuplicateExistsAsync(string userId, Guid permissionId, Guid orgUnitId, string scopeMode, Guid? excludeId, CancellationToken ct = default);
}
