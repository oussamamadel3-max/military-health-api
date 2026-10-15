using Application.Exceptions;

namespace Application.Common.Permissions;

/// <summary>
/// يحمي أدوار SRS §6.2 المزروعة (الثمانية) من التعديل الخطير:
/// إعادة التسمية، وتعطيل/إلغاء دور مدير النظام المركزي المحجوز.
/// </summary>
public static class ReservedRoleGuard
{
    public const string CentralAdminRoleName = "SuperAdmin";

    private static readonly HashSet<string> ReservedNames = MilitaryHealthSeedCatalog.Roles
        .Select(r => r.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsReserved(string? roleName) =>
        !string.IsNullOrWhiteSpace(roleName) && ReservedNames.Contains(roleName.Trim());

    public static bool IsCentralAdminRole(string? roleName) =>
        string.Equals(roleName?.Trim(), CentralAdminRoleName, StringComparison.OrdinalIgnoreCase);

    public static void EnsureCanRename(string? currentName, string? newName)
    {
        if (!IsReserved(currentName))
            return;

        if (!string.Equals(currentName?.Trim(), newName?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenPermissionException("لا يمكن إعادة تسمية الأدوار الأساسية للنظام.");
    }

    public static void EnsureCanDeactivate(string? roleName)
    {
        if (IsCentralAdminRole(roleName))
            throw new ForbiddenPermissionException("لا يمكن تعطيل دور مدير النظام المركزي المحجوز.");
    }
}
