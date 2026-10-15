using Application.Exceptions;
using Application.Interfaces.Repositories;

namespace Application.Common.Security;

public static class UserManagementScopeGuard
{
    public static async Task<bool> ActorHasUnrestrictedScopeAsync(
        IAccessContextRepository access,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await access.LoadAsync(actorUserId, cancellationToken);
        return CenterPermissionEvaluator.HasCentralAuthority(snapshot.Roles, snapshot.Directs);
    }

    public static async Task EnsureCanManageTargetAsync(
        IAccessContextRepository access,
        IUserRepository users,
        string actorUserId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ForbiddenPermissionException("لا يمكنك إدارة مستخدم خارج نطاق تفويضك.");

        if (await ActorHasUnrestrictedScopeAsync(access, actorUserId, cancellationToken))
            return;

        var targetSnapshot = await access.LoadAsync(targetUserId, cancellationToken);
        if (CenterPermissionEvaluator.HasCentralAuthority(targetSnapshot.Roles, targetSnapshot.Directs))
            throw new ForbiddenPermissionException("لا يمكنك إدارة مستخدم مركزي خارج نطاق تفويضك.");

        if (!await users.SharesActiveCenterAsync(actorUserId, targetUserId, cancellationToken))
            throw new ForbiddenPermissionException("لا يمكنك إدارة مستخدم خارج نطاق مراكزك.");
    }
}
