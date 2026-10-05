using Application.Exceptions;
using Application.Interfaces.Repositories;

namespace Application.Common.Permissions;

/// <summary>
/// يحمي آخر مستخدم نشط قادر على إدارة المستخدمين والأدوار والصلاحيات معاً.
/// </summary>
public static class IamAdminGuard
{
    public static bool IsIamAdministrator(IEnumerable<string?> permissionNames)
    {
        var set = permissionNames
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool hasUser = SystemPermissions.UserManagementWrite.Any(set.Contains);
        bool hasRole = SystemPermissions.RoleManagementWrite.Any(set.Contains);
        bool hasPerm = SystemPermissions.PermissionManagementWrite.Any(set.Contains);
        return hasUser && hasRole && hasPerm;
    }

    public static async Task EnsureNotLastIamAdminAsync(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        string targetUserId,
        IReadOnlyCollection<string> targetPermissionsAfterChange,
        CancellationToken cancellationToken = default)
    {
        if (IsIamAdministrator(targetPermissionsAfterChange))
            return;

        var otherAdminExists = await AnyOtherIamAdminAsync(
            userRepository, roleRepository, targetUserId, cancellationToken);

        if (!otherAdminExists)
            throw new ForbiddenPermissionException(
                "لا يمكن إزالة أو تعطيل أو سحب قدرات آخر حساب نشط قادر على إدارة المستخدمين والأدوار والصلاحيات.");
    }

    public static async Task EnsureNotLastIamAdminBeforeDisableOrDeleteAsync(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        var current = await GetEffectivePermissionNamesAsync(
            userRepository, roleRepository, targetUserId, cancellationToken);

        if (!IsIamAdministrator(current))
            return;

        var otherAdminExists = await AnyOtherIamAdminAsync(
            userRepository, roleRepository, targetUserId, cancellationToken);

        if (!otherAdminExists)
            throw new ForbiddenPermissionException(
                "لا يمكن حذف أو تعطيل آخر حساب نشط قادر على إدارة المستخدمين والأدوار والصلاحيات.");
    }

    public static async Task<HashSet<string>> GetEffectivePermissionNamesAsync(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var direct = await userRepository.GetDirectPermissionNamesAsync(userId, activeOnly: true);
        var roleIds = await userRepository.GetActiveRoleIdsAsync(userId);
        var rolePerms = (await roleRepository.GetRolePermissionsForRolesAsync(roleIds))
            .Select(rp => rp.Permission?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!);

        return direct.Concat(rolePerms).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<bool> AnyOtherIamAdminAsync(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        string excludeUserId,
        CancellationToken cancellationToken)
    {
        var activeUsers = await userRepository.GetActiveUserIdsExceptAsync(excludeUserId, cancellationToken);
        foreach (var userId in activeUsers)
        {
            var perms = await GetEffectivePermissionNamesAsync(
                userRepository, roleRepository, userId, cancellationToken);
            if (IsIamAdministrator(perms))
                return true;
        }

        return false;
    }

    public static void EnsureAssignableWithinActorPermissions(
        IEnumerable<string?> requestedPermissionNames,
        IReadOnlyCollection<string> actorPermissions)
    {
        var actorSet = actorPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excess = requestedPermissionNames
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !actorSet.Contains(p))
            .ToList();

        if (excess.Count > 0)
            throw new ForbiddenPermissionException(
                $"لا يمكن منح صلاحيات تتجاوز حدود تفويضك: {string.Join(", ", excess)}");
    }
}
