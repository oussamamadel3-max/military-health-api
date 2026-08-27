using Application.Common;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;

namespace Application.Services.Core;

public class OrgScopeResolver : IOrgScopeResolver
{
    private readonly IUserPermissionService _permissions;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserOrgScopeRepository _scopes;
    private readonly IOrgUnitRepository _units;

    public OrgScopeResolver(IUserPermissionService permissions, IPermissionRepository permissionRepository,
        IUserOrgScopeRepository scopes, IOrgUnitRepository units) =>
        (_permissions, _permissionRepository, _scopes, _units) = (permissions, permissionRepository, scopes, units);

    public bool UserHasBasePermission(string userId, string permissionName) =>
        !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(permissionName) &&
        _permissions.HasPermission(userId, permissionName.Trim());

    public async Task<IReadOnlyList<Guid>> GetAccessibleOrgUnitIdsAsync(
        string userId, string permissionName, CancellationToken ct = default)
    {
        if (!UserHasBasePermission(userId, permissionName)) return Array.Empty<Guid>();
        var permissionId = await _permissionRepository.FindActivePermissionIdByNameAsync(permissionName.Trim(), ct);
        if (!permissionId.HasValue) return Array.Empty<Guid>();
        var scopes = await _scopes.GetActiveScopesForPermissionAsync(userId, permissionId.Value, ct);
        if (scopes.Count == 0) return Array.Empty<Guid>();
        var links = await _units.GetNonArchivedParentLinksAsync(ct);
        return ResolveScopedOrgUnitIds(scopes, links);
    }

    public async Task<bool> CanAccessOrgUnitAsync(string userId, string permissionName, Guid orgUnitId, CancellationToken ct = default) =>
        (await GetAccessibleOrgUnitIdsAsync(userId, permissionName, ct)).Contains(orgUnitId);

    public static IReadOnlyList<Guid> ResolveScopedOrgUnitIds(
        IEnumerable<OrgScopeLink> scopes, IEnumerable<(Guid Id, Guid? ParentId)> nonArchivedLinks)
    {
        var children = OrgUnitTreeHelper.BuildChildrenMap(nonArchivedLinks);
        var result = new HashSet<Guid>();
        foreach (var scope in scopes)
        {
            result.Add(scope.OrgUnitId);
            if (scope.ScopeMode == ScopeModes.Subtree)
                result.UnionWith(OrgUnitTreeHelper.GetDescendantIds(scope.OrgUnitId, children));
        }
        return result.ToList();
    }
}
