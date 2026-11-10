using Application.Exceptions;
using Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Application.Common.Security;

/// <summary>
/// يمنع فقدان آخر مدير مركز فعّال. الهوية هي اسم الدور المزروع CenterManager،
/// والحساب يُحسب فقط مع مستخدم نشط ودور نشط وإسناد مركز نشط.
/// </summary>
public static class CenterManagerContinuityGuard
{
    public const string RoleName = "CenterManager";

    public const string LastCenterManagerMessage =
        "لا يمكن إتمام العملية لأن المركز سيفقد آخر مدير فعّال. أسند مديراً بديلاً أولاً.";

    public static async Task EnsureDeactivatingUserAsync(
        IUserRepository users,
        IRoleRepository roles,
        IMedicalCenterRepository centers,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var centerIds = await CentersManagedByAsync(users, roles, userId, cancellationToken);
        foreach (var centerId in centerIds.Order())
            await EnsureOthersRemainAsync(users, roles, centers, centerId, userId, cancellationToken);
    }

    public static async Task EnsureRevokingRoleLinkAsync(
        IUserRepository users,
        IRoleRepository roles,
        IMedicalCenterRepository centers,
        string userId,
        string roleName,
        Guid? assignmentCenterId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(roleName, RoleName, StringComparison.Ordinal))
            return;

        if (assignmentCenterId is Guid centerId)
        {
            await EnsureOthersRemainAsync(users, roles, centers, centerId, userId, cancellationToken);
            return;
        }

        var centersOfUser = await MaterializeAsync(
            users.QueryUserCenters()
                .Where(uc => uc.UserId == userId && uc.IsActive && uc.Center.IsActive)
                .Select(uc => uc.CenterId),
            cancellationToken);

        foreach (var id in centersOfUser.Order())
            await EnsureOthersRemainAsync(users, roles, centers, id, userId, cancellationToken);
    }

    public static async Task EnsureRevokingCenterAsync(
        IUserRepository users,
        IRoleRepository roles,
        IMedicalCenterRepository centers,
        string userId,
        Guid centerId,
        CancellationToken cancellationToken = default)
    {
        var managed = await CentersManagedByAsync(users, roles, userId, cancellationToken);
        if (!managed.Contains(centerId))
            return;

        await EnsureOthersRemainAsync(users, roles, centers, centerId, userId, cancellationToken);
    }

    public static async Task EnsureDeactivatingRoleAsync(
        IUserRepository users,
        IRoleRepository roles,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(roleName, RoleName, StringComparison.Ordinal))
            return;

        var roleId = await ActiveRoleIdAsync(roles, cancellationToken);
        if (roleId is null)
            return;

        var covered = await CoveredCenterIdsAsync(users, roleId, excludeUserId: null, cancellationToken);
        if (covered.Count > 0)
            throw new ForbiddenPermissionException(LastCenterManagerMessage);
    }

    private static async Task EnsureOthersRemainAsync(
        IUserRepository users,
        IRoleRepository roles,
        IMedicalCenterRepository centers,
        Guid centerId,
        string excludeUserId,
        CancellationToken cancellationToken)
    {
        await centers.LockAsync(centerId, cancellationToken);
        var others = await CountManagersAsync(users, roles, centerId, excludeUserId, cancellationToken);
        if (others == 0)
            throw new ForbiddenPermissionException(LastCenterManagerMessage);
    }

    private static async Task<List<Guid>> CentersManagedByAsync(
        IUserRepository users,
        IRoleRepository roles,
        string userId,
        CancellationToken cancellationToken)
    {
        var roleId = await ActiveRoleIdAsync(roles, cancellationToken);
        if (roleId is null)
            return [];

        var links = await MaterializeAsync(
            users.QueryRoleAssociations()
                .Where(a => a.UserId == userId && a.IsActive && a.RoleId == roleId && a.Role.IsActive && a.User.IsActive)
                .Select(a => a.CenterId),
            cancellationToken);

        if (links.Count == 0)
            return [];

        var assigned = await MaterializeAsync(
            users.QueryUserCenters()
                .Where(uc => uc.UserId == userId && uc.IsActive && uc.Center.IsActive)
                .Select(uc => uc.CenterId),
            cancellationToken);

        var result = new HashSet<Guid>();
        if (links.Any(c => c is null))
        {
            foreach (var id in assigned)
                result.Add(id);
        }

        foreach (var centerId in links.OfType<Guid>())
        {
            if (assigned.Contains(centerId))
                result.Add(centerId);
        }

        return result.ToList();
    }

    private static async Task<int> CountManagersAsync(
        IUserRepository users,
        IRoleRepository roles,
        Guid centerId,
        string? excludeUserId,
        CancellationToken cancellationToken)
    {
        var ids = await CoveredCenterIdsAsync(users, await ActiveRoleIdAsync(roles, cancellationToken), excludeUserId, cancellationToken);
        return ids.Contains(centerId)
            ? await ManagerCountForCenterAsync(users, roles, centerId, excludeUserId, cancellationToken)
            : 0;
    }

    private static async Task<int> ManagerCountForCenterAsync(
        IUserRepository users,
        IRoleRepository roles,
        Guid centerId,
        string? excludeUserId,
        CancellationToken cancellationToken)
    {
        var roleId = await ActiveRoleIdAsync(roles, cancellationToken);
        if (roleId is null)
            return 0;

        var candidates = users.QueryRoleAssociations()
            .Where(a => a.IsActive && a.RoleId == roleId && a.Role.IsActive && a.User.IsActive)
            .Where(a => a.CenterId == centerId || a.CenterId == null);

        if (!string.IsNullOrEmpty(excludeUserId))
            candidates = candidates.Where(a => a.UserId != excludeUserId);

        var userIds = await MaterializeAsync(candidates.Select(a => a.UserId).Distinct(), cancellationToken);
        if (userIds.Count == 0)
            return 0;

        return await CountMaterializedAsync(
            users.QueryUserCenters()
                .Where(uc => uc.CenterId == centerId && uc.IsActive && uc.Center.IsActive && userIds.Contains(uc.UserId))
                .Select(uc => uc.UserId)
                .Distinct(),
            cancellationToken);
    }

    private static async Task<HashSet<Guid>> CoveredCenterIdsAsync(
        IUserRepository users,
        string? roleId,
        string? excludeUserId,
        CancellationToken cancellationToken)
    {
        if (roleId is null)
            return [];

        var links = users.QueryRoleAssociations()
            .Where(a => a.IsActive && a.RoleId == roleId && a.Role.IsActive && a.User.IsActive);
        if (!string.IsNullOrEmpty(excludeUserId))
            links = links.Where(a => a.UserId != excludeUserId);

        var rows = await MaterializeAsync(links.Select(a => new { a.UserId, a.CenterId }), cancellationToken);
        var assigned = await MaterializeAsync(
            users.QueryUserCenters()
                .Where(uc => uc.IsActive && uc.Center.IsActive)
                .Select(uc => new { uc.UserId, uc.CenterId }),
            cancellationToken);

        var assignedByUser = assigned.GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.CenterId).ToHashSet());
        var covered = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (!assignedByUser.TryGetValue(row.UserId, out var centers))
                continue;
            if (row.CenterId is null)
            {
                foreach (var id in centers)
                    covered.Add(id);
            }
            else if (centers.Contains(row.CenterId.Value))
            {
                covered.Add(row.CenterId.Value);
            }
        }

        return covered;
    }

    private static async Task<string?> ActiveRoleIdAsync(IRoleRepository roles, CancellationToken cancellationToken)
    {
        var query = roles.QueryRoles()
            .Where(r => r.Name == RoleName && r.IsActive)
            .Select(r => r.Id);
        return await FirstAsync(query, cancellationToken);
    }

    private static Task<List<T>> MaterializeAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        => query.Provider is IAsyncQueryProvider
            ? query.ToListAsync(cancellationToken)
            : Task.FromResult(query.ToList());

    private static Task<T?> FirstAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        => query.Provider is IAsyncQueryProvider
            ? query.FirstOrDefaultAsync(cancellationToken)
            : Task.FromResult(query.FirstOrDefault());

    private static Task<int> CountMaterializedAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        => query.Provider is IAsyncQueryProvider
            ? query.CountAsync(cancellationToken)
            : Task.FromResult(query.Count());
}
