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

    /// <summary>
    /// مدير المركز لا يسند مركزاً خارج مراكزه الفعّالة (FR-019 / BR-048).
    /// </summary>
    public static async Task EnsureCanAssignCenterAsync(
        IAccessContextRepository access,
        string actorUserId,
        Guid centerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ForbiddenPermissionException("لا يمكنك إسناد مركز خارج نطاق تفويضك.");

        if (await ActorHasUnrestrictedScopeAsync(access, actorUserId, cancellationToken))
            return;

        if (!await access.IsActivelyAssignedAsync(actorUserId, centerId, cancellationToken))
            throw new ForbiddenPermissionException("لا يمكنك إسناد مستخدم إلى مركز خارج نطاق تفويضك.");
    }

    /// <summary>
    /// الإسناد المركزي (CenterId فارغ) يتطلب سلطة مركزية للمنفّذ (BR-048).
    /// </summary>
    public static async Task EnsureCanAssignRoleScopeAsync(
        IAccessContextRepository access,
        string actorUserId,
        Guid? roleCenterId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ForbiddenPermissionException("لا يمكنك إسناد دور خارج نطاق تفويضك.");

        if (await ActorHasUnrestrictedScopeAsync(access, actorUserId, cancellationToken))
            return;

        if (roleCenterId is null)
            throw new ForbiddenPermissionException("لا يمكنك إسناد دور مركزي خارج نطاق تفويضك.");

        if (!await access.IsActivelyAssignedAsync(actorUserId, roleCenterId.Value, cancellationToken))
            throw new ForbiddenPermissionException("لا يمكنك إسناد دور لمركز خارج نطاق تفويضك.");
    }
}
