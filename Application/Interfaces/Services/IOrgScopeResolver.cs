namespace Application.Interfaces.Services;

public interface IOrgScopeResolver
{
    bool UserHasBasePermission(string userId, string permissionName);
    Task<IReadOnlyList<Guid>> GetAccessibleOrgUnitIdsAsync(string userId, string permissionName, CancellationToken ct = default);
    Task<bool> CanAccessOrgUnitAsync(string userId, string permissionName, Guid orgUnitId, CancellationToken ct = default);
}
