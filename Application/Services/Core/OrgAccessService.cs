using Application.Interfaces;
using Application.Interfaces.Services;

namespace Application.Services.Core;

public class OrgAccessService : IOrgAccessService
{
    private readonly IOrgScopeResolver _resolver;
    private readonly IActorScopeProvider _actorScope;

    public OrgAccessService(IOrgScopeResolver resolver, IActorScopeProvider actorScope) =>
        (_resolver, _actorScope) = (resolver, actorScope);

    public async Task<OrgAccessResult> GetAccessAsync(string userId, string permissionName, CancellationToken ct = default)
    {
        if (!_resolver.UserHasBasePermission(userId, permissionName))
            return new(false, false, Array.Empty<Guid>());
        if (await _actorScope.IsSuperAdminAsync(userId, ct))
            return new(true, true, Array.Empty<Guid>());
        return new(true, false, await _resolver.GetAccessibleOrgUnitIdsAsync(userId, permissionName, ct));
    }
}
