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

    /// <summary>
    /// يمنع سحب صلاحيات إدارية من دور إذا كان ذلك يُفقد النظام آخر حساب إداري فعّال لـ IAM.
    /// </summary>
    public static async Task EnsureAtLeastOneIamAdminRemainsAfterRolePermissionChangeAsync(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        string changedRoleId,
        IReadOnlyCollection<string> changedRolePermissionNamesAfter,
        IReadOnlyCollection<string>? previousRolePermissionNames = null,
        CancellationToken cancellationToken = default)
    {
        var afterRolePerms = Normalize(changedRolePermissionNamesAfter);

        if (previousRolePermissionNames is not null)
        {
            var before = Normalize(previousRolePermissionNames);
            if (before.IsSubsetOf(afterRolePerms))
                return;
        }

        if (IsIamAdministrator(afterRolePerms))
            return;

        var activeUserIds = await userRepository.GetActiveUserIdsExceptAsync(string.Empty, cancellationToken);
        var hadAdmin = false;
        var remainsAdmin = false;

        foreach (var userId in activeUserIds)
        {
            var before = await GetEffectivePermissionNamesAsync(
                userRepository, roleRepository, userId, cancellationToken);
            if (IsIamAdministrator(before))
                hadAdmin = true;

            var after = await GetEffectivePermissionNamesWithRoleOverrideAsync(
                userRepository, roleRepository, userId, changedRoleId, afterRolePerms, cancellationToken);
            if (IsIamAdministrator(after))
                remainsAdmin = true;
        }

        if (hadAdmin && !remainsAdmin)
            throw new ForbiddenPermissionException(
                "لا يمكن إزالة أو تعطيل قدرات آخر حساب نشط قادر على إدارة المستخدمين والأدوار والصلاحيات.");
    }

    public static async Task<HashSet<string>> GetEffectivePermissionNamesWithRoleOverrideAsync(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        string userId,
        string overriddenRoleId,
        IReadOnlyCollection<string> overriddenRolePermissionNames,
        CancellationToken cancellationToken = default)
    {
        var direct = await userRepository.GetDirectPermissionNamesAsync(userId, activeOnly: true, cancellationToken);
        var roleIds = await userRepository.GetActiveRoleIdsAsync(userId, cancellationToken);
        var otherRoleIds = roleIds
            .Where(id => !string.Equals(id, overriddenRoleId, StringComparison.Ordinal))
            .ToList();

        var otherPerms = otherRoleIds.Count == 0
            ? Enumerable.Empty<string>()
            : (await roleRepository.GetRolePermissionsForRolesAsync(otherRoleIds, cancellationToken))
                .Select(rp => rp.Permission?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!);

        var hasOverriddenRole = roleIds.Any(id => string.Equals(id, overriddenRoleId, StringComparison.Ordinal));
        var fromOverridden = hasOverriddenRole
            ? overriddenRolePermissionNames
            : Enumerable.Empty<string>();

        return Normalize(direct.Concat(otherPerms).Concat(fromOverridden));
    }

    private static HashSet<string> Normalize(IEnumerable<string?> names) =>
        names
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
