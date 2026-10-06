using Application.Interfaces.Services;

namespace Application.Services.Core;

public class OrgAccessService : IOrgAccessService
{
    private readonly IOrgScopeResolver _resolver;

    public OrgAccessService(IOrgScopeResolver resolver) => _resolver = resolver;

    public async Task<OrgAccessResult> GetAccessAsync(string userId, string permissionName, CancellationToken ct = default)
    {
        if (!_resolver.UserHasBasePermission(userId, permissionName))
            return new(false, false, Array.Empty<Guid>());

        var orgUnitIds = await _resolver.GetAccessibleOrgUnitIdsAsync(userId, permissionName, ct);
        // Permission without org scopes = global access
        return new(true, orgUnitIds.Count == 0, orgUnitIds);
    }
}
