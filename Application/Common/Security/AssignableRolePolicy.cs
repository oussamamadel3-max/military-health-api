using Application.Exceptions;

namespace Application.Common.Security;

/// <summary>
/// سياسة إسناد الأدوار المشتركة بين القائمة والحفظ.
/// مدير المركز يسند أدواراً تشغيلية محلية معتمدة فقط. مدير المركز والمسؤول مؤجلان حتى حسم رسمي.
/// الدور المركزي (بلا مركز) مقتصر على أدوار الكتالوج المركزي، ولجهة مركزية فقط.
/// </summary>
public static class AssignableRolePolicy
{
    public static readonly string[] LocalOperationalRoleNames =
    [
        "ReceptionClerk",
        "ClinicDoctor",
        "Supervisor",
        "ArchiveClerk",
    ];

    public static readonly string[] CentralScopeRoleNames =
    [
        "SuperAdmin",
        "Auditor",
    ];

    private static readonly HashSet<string> LocalOperational =
        new(LocalOperationalRoleNames, StringComparer.Ordinal);

    private static readonly HashSet<string> CentralScope =
        new(CentralScopeRoleNames, StringComparer.Ordinal);

    public static bool IsLocalOperational(string? roleName) =>
        !string.IsNullOrWhiteSpace(roleName) && LocalOperational.Contains(roleName);

    public static bool IsCentralScopeRole(string? roleName) =>
        !string.IsNullOrWhiteSpace(roleName) && CentralScope.Contains(roleName);

    public static void EnsureAssignable(bool actorIsCentral, string? roleName, bool roleIsActive)
    {
        if (!roleIsActive)
            throw new InvalidUserDataException("لا يمكن إسناد دور معطّل.");

        if (string.IsNullOrWhiteSpace(roleName))
            throw new InvalidUserDataException("الدور غير موجود.");

        if (actorIsCentral)
            return;

        if (!IsLocalOperational(roleName))
            throw new ForbiddenPermissionException("لا يمكنك إسناد هذا الدور ضمن حدود تفويضك.");
    }

    public static Guid? ResolveSyncScope(
        bool actorIsCentral,
        IReadOnlyCollection<string> roleNames,
        Guid? requestedCenterId,
        Guid? activeCenterId,
        bool actorInCentralContext)
    {
        if (requestedCenterId == Guid.Empty)
            throw new InvalidUserDataException("معرف المركز غير صالح.");

        var names = roleNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var hasCentralRole = names.Any(IsCentralScopeRole);
        var hasScopedRole = names.Any(n => !IsCentralScopeRole(n));
        if (hasCentralRole && hasScopedRole)
            throw new InvalidUserDataException("لا يمكن الجمع بين دور مركزي ودور مقيد بمركز في الطلب نفسه.");

        if (!actorIsCentral)
        {
            if (actorInCentralContext || activeCenterId is null || activeCenterId == Guid.Empty)
                throw new ForbiddenPermissionException("لا يوجد مركز نشط لإسناد الدور.");

            if (requestedCenterId is Guid requested && requested != activeCenterId.Value)
                throw new ForbiddenPermissionException("لا يمكنك إسناد دور خارج مركزك النشط.");

            return activeCenterId.Value;
        }

        if (hasCentralRole || (names.Count == 0 && requestedCenterId is null))
        {
            if (requestedCenterId is not null)
                throw new InvalidUserDataException("إسناد الدور المركزي لا يُقيَّد بمركز.");

            return null;
        }

        if (requestedCenterId is null)
            throw new InvalidUserDataException("إسناد هذا الدور يتطلب مركزاً فعّالاً.");

        return requestedCenterId.Value;
    }

    /// <summary>
    /// الإلغاء الصريح: الجهة المركزية تستطيع استهداف صف بلا مركز لتنظيف إسناد سابق.
    /// الإسناد الجديد لا يستخدم هذا المسار.
    /// </summary>
    public static Guid? ResolveRevokeScope(
        bool actorIsCentral,
        string roleName,
        Guid? requestedCenterId,
        Guid? activeCenterId,
        bool actorInCentralContext)
    {
        if (actorIsCentral)
        {
            if (requestedCenterId == Guid.Empty)
                throw new InvalidUserDataException("معرف المركز غير صالح.");

            return requestedCenterId;
        }

        return ResolveSyncScope(
            actorIsCentral: false,
            roleNames: [roleName],
            requestedCenterId,
            activeCenterId,
            actorInCentralContext);
    }

    public static bool MayRevoke(bool actorIsCentral, string? roleName, Guid? scopeCenterId)
    {
        if (!actorIsCentral)
            return IsLocalOperational(roleName);

        if (scopeCenterId is null)
            return IsCentralScopeRole(roleName);

        return true;
    }
}
