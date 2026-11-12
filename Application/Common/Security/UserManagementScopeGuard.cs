using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;

namespace Application.Common.Security;

/// <summary>
/// حراس نطاق إدارة المستخدمين والإسنادات.
/// النطاق غير المقيد يُمنح فقط عند امتلاك <b>صلاحية العملية نفسها</b> بنطاق مركزي فعّال (BR-048 / BR-057 / NFR-018).
/// </summary>
public static class UserManagementScopeGuard
{
    /// <summary>
    /// هل يملك المنفّذ الصلاحية المحددة ضمن سياق مركزي (CenterId فارغ على الدور أو المنح المباشر)؟
    /// </summary>
    public static async Task<bool> ActorHasCentralOperationScopeAsync(
        IAccessContextRepository access,
        string actorUserId,
        string operationPermission,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) || string.IsNullOrWhiteSpace(operationPermission))
            return false;

        var snapshot = await access.LoadAsync(actorUserId, cancellationToken);
        return CenterPermissionEvaluator.HasCentralOperationPermission(
            snapshot.Roles,
            snapshot.Directs,
            snapshot.Links,
            operationPermission);
    }

    public static async Task EnsureCanManageTargetAsync(
        IAccessContextRepository access,
        IUserRepository users,
        string actorUserId,
        string targetUserId,
        string operationPermission,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ForbiddenPermissionException("لا يمكنك إدارة مستخدم خارج نطاق تفويضك.");

        if (string.IsNullOrWhiteSpace(operationPermission))
            throw new ForbiddenPermissionException("لا يمكنك إدارة مستخدم خارج نطاق تفويضك.");

        if (await ActorHasCentralOperationScopeAsync(access, actorUserId, operationPermission, cancellationToken))
            return;

        var targetSnapshot = await access.LoadAsync(targetUserId, cancellationToken);
        if (CenterPermissionEvaluator.HasCentralAuthority(targetSnapshot.Roles, targetSnapshot.Directs))
            throw new ForbiddenPermissionException("لا يمكنك إدارة مستخدم مركزي خارج نطاق تفويضك.");

        if (!await users.SharesActiveCenterAsync(actorUserId, targetUserId, cancellationToken))
            throw new ForbiddenPermissionException("لا يمكنك إدارة مستخدم خارج نطاق مراكزك.");
    }

    /// <summary>
    /// مدير المركز لا يسند مركزاً خارج مراكزه الفعّالة (FR-019 / BR-048).
    /// التجاوز المركزي يتطلب <paramref name="operationPermission"/> بنطاق مركزي (عادة UserCenters.Manage).
    /// </summary>
    public static async Task EnsureCanAssignCenterAsync(
        IAccessContextRepository access,
        string actorUserId,
        Guid centerId,
        string operationPermission,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ForbiddenPermissionException("لا يمكنك إسناد مركز خارج نطاق تفويضك.");

        if (await ActorHasCentralOperationScopeAsync(access, actorUserId, operationPermission, cancellationToken))
            return;

        if (!await access.IsActivelyAssignedAsync(actorUserId, centerId, cancellationToken))
            throw new ForbiddenPermissionException("لا يمكنك إسناد مستخدم إلى مركز خارج نطاق تفويضك.");
    }

    /// <summary>
    /// الإسناد المركزي (CenterId فارغ) يتطلب صلاحية العملية بنطاق مركزي للمنفّذ (BR-048).
    /// </summary>
    public static async Task EnsureCanAssignRoleScopeAsync(
        IAccessContextRepository access,
        string actorUserId,
        Guid? roleCenterId,
        string operationPermission,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ForbiddenPermissionException("لا يمكنك إسناد دور خارج نطاق تفويضك.");

        if (await ActorHasCentralOperationScopeAsync(access, actorUserId, operationPermission, cancellationToken))
            return;

        if (roleCenterId is null)
            throw new ForbiddenPermissionException("لا يمكنك إسناد دور مركزي خارج نطاق تفويضك.");

        if (!await access.IsActivelyAssignedAsync(actorUserId, roleCenterId.Value, cancellationToken))
            throw new ForbiddenPermissionException("لا يمكنك إسناد دور لمركز خارج نطاق تفويضك.");
    }

    /// <summary>
    /// يتحقق من حق المنفّذ في قراءة بيانات إسناد/صلاحيات مركز محدد (قوائم FR-018) — ليس لعمليات الإسناد.
    /// </summary>
    public static async Task EnsureCanViewCenterDataAsync(
        IAccessContextRepository access,
        IMedicalCenterRepository centers,
        string actorUserId,
        Guid centerId,
        ICurrentCenterService currentCenter,
        string operationPermission,
        CancellationToken cancellationToken = default)
    {
        if (centerId == Guid.Empty)
            throw new InvalidUserDataException("معرف المركز غير صالح.");

        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ForbiddenPermissionException("لا يمكنك عرض بيانات مركز خارج نطاق تفويضك.");

        if (await ActorHasCentralOperationScopeAsync(access, actorUserId, operationPermission, cancellationToken))
        {
            _ = await centers.GetByIdAsync(centerId, asNoTracking: true, cancellationToken)
                ?? throw new MedicalCenterNotFoundException();
            return;
        }

        if (currentCenter.IsCentralContext)
            throw new ForbiddenPermissionException("لا يمكن عرض إسنادات المركز دون سياق مركز نشط.");

        if (currentCenter.CenterId is not Guid active || active != centerId)
            throw new ForbiddenPermissionException("لا يمكنك عرض إسنادات مركز غير المركز النشط.");

        if (!await access.IsActivelyAssignedAsync(actorUserId, centerId, cancellationToken))
            throw new ForbiddenPermissionException("لا يمكنك عرض بيانات مركز غير مسند لك.");
    }
}
