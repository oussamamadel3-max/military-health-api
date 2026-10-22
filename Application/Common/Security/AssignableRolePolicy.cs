using Application.Exceptions;
using Domain.Enums;

namespace Application.Common.Security;

/// <summary>
/// سياسة إسناد الأدوار المشتركة بين القائمة والحفظ.
/// مدير المركز يسند أدواراً تشغيلية محلية معتمدة فقط.
/// النطاق التشغيلي المخزَّن على الدور يحدد إن كان الإسناد مركزياً أو مقيداً بمركز.
/// أدوار الكتالوج النظامية تُثبَّت بنطاقها عند البذر؛ الأدوار المخصصة تعتمد على Scope المخزَّن.
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

    /// <summary>أسماء أدوار الكتالوج المركزية — تثبيت أولي عند البذر والاختبارات.</summary>
    public static readonly string[] CentralScopeRoleNames =
    [
        "SuperAdmin",
        "Auditor",
    ];

    private static readonly HashSet<string> LocalOperational =
        new(LocalOperationalRoleNames, StringComparer.Ordinal);

    private static readonly HashSet<string> CentralCatalogNames =
        new(CentralScopeRoleNames, StringComparer.Ordinal);

    public static bool IsLocalOperational(string? roleName) =>
        !string.IsNullOrWhiteSpace(roleName) && LocalOperational.Contains(roleName);

    public static bool IsCentralScope(RoleScope scope) => scope == RoleScope.Central;

    /// <summary>استدلال نطاق الكتالوج من الاسم عند غياب القيمة المخزَّنة (بذر/اختبارات).</summary>
    public static RoleScope CatalogScopeForName(string? roleName) =>
        !string.IsNullOrWhiteSpace(roleName) && CentralCatalogNames.Contains(roleName)
            ? RoleScope.Central
            : RoleScope.Center;

    public static void EnsureAssignable(
        bool actorIsCentral,
        string? roleName,
        bool roleIsActive,
        RoleScope scope)
    {
        if (!roleIsActive)
            throw new InvalidUserDataException("لا يمكن إسناد دور معطّل.");

        if (string.IsNullOrWhiteSpace(roleName))
            throw new InvalidUserDataException("الدور غير موجود.");

        if (actorIsCentral)
            return;

        if (IsCentralScope(scope))
            throw new ForbiddenPermissionException("لا يمكنك إسناد دور مركزي ضمن حدود تفويضك.");

        if (!IsLocalOperational(roleName))
            throw new ForbiddenPermissionException("لا يمكنك إسناد هذا الدور ضمن حدود تفويضك.");
    }

    public static Guid? ResolveSyncScope(
        bool actorIsCentral,
        IReadOnlyCollection<RoleScope> roleScopes,
        Guid? requestedCenterId,
        Guid? activeCenterId,
        bool actorInCentralContext)
    {
        if (requestedCenterId == Guid.Empty)
            throw new InvalidUserDataException("معرف المركز غير صالح.");

        var scopes = roleScopes.ToList();
        var hasCentralRole = scopes.Any(IsCentralScope);
        var hasScopedRole = scopes.Any(s => !IsCentralScope(s));
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

        if (hasCentralRole || (scopes.Count == 0 && requestedCenterId is null))
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
        RoleScope roleScope,
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
            roleScopes: [roleScope],
            requestedCenterId,
            activeCenterId,
            actorInCentralContext);
    }

    public static bool MayRevoke(
        bool actorIsCentral,
        string? roleName,
        Guid? scopeCenterId,
        RoleScope roleScope)
    {
        if (!actorIsCentral)
            return IsLocalOperational(roleName);

        if (scopeCenterId is null)
            return IsCentralScope(roleScope);

        return true;
    }
}
