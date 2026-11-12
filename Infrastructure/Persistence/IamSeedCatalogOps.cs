using Application.Common.Security;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// Idempotent IAM catalog seed helpers that create missing rows only.
/// Administrative deactivation of a Role/Permission or withdrawal of a RolePermission/UserPermission is preserved.
/// Intentional migrations may still call <see cref="IamRetention.Reactivate"/> outside this type.
/// </summary>
public static class IamSeedCatalogOps
{
    public const string SystemSeedUserId = "0";
    public const string SystemSeedUserName = "SYSTEM_SEED";
    public const string SystemSeedUserAgent = "SYSTEM_SEED";

    /// <summary>
    /// Marks subsequent audit writes as automatic seed work (null ActorUserId, no client IP).
    /// </summary>
    public static void ApplySystemSeedAuditContext(ICurrentUserService currentUser)
    {
        currentUser.SetUser(SystemSeedUserId, SystemSeedUserName, string.Empty);
        currentUser.SetRequest(SystemSeedUserAgent, Guid.NewGuid());
    }

    /// <summary>
    /// Creates a missing seed role, or updates description/scope metadata only.
    /// Never reactivates an administratively deactivated role (FR-016 / BR-047).
    /// </summary>
    public static async Task<Role> EnsureRoleAsync(
        AppDbContext context,
        string name,
        string normalized,
        string? descriptionAr = null,
        RoleScope scope = RoleScope.Center,
        CancellationToken cancellationToken = default)
    {
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
        if (role == null)
        {
            role = new Role
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                NormalizedName = normalized,
                Description = descriptionAr,
                Scope = scope,
                IsActive = true,
                ConcurrencyStamp = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.Now
            };
            context.Roles.Add(role);
            await context.SaveChangesAsync(cancellationToken);
            return role;
        }

        var dirty = false;
        if (!string.IsNullOrWhiteSpace(descriptionAr) &&
            !string.Equals(role.Description, descriptionAr, StringComparison.Ordinal))
        {
            role.Description = descriptionAr;
            dirty = true;
        }

        if (role.Scope != scope)
        {
            role.Scope = scope;
            dirty = true;
        }

        if (dirty)
            await context.SaveChangesAsync(cancellationToken);

        return role;
    }

    /// <summary>
    /// Creates a missing catalog permission. Never reactivates an admin-deactivated row.
    /// </summary>
    public static async Task<Permission> EnsurePermissionAsync(
        AppDbContext context,
        string name,
        string? desc,
        CancellationToken cancellationToken = default)
    {
        var perm = await context.Permissions.FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
        if (perm != null)
            return perm;

        perm = new Permission
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = desc,
            IsActive = true
        };
        context.Permissions.Add(perm);
        await context.SaveChangesAsync(cancellationToken);
        return perm;
    }

    /// <summary>
    /// Ensures active matrix grants exist for the role. Creates missing active links only.
    /// Does not reactivate an administratively revoked RolePermission.
    /// Skips catalog-inactive permissions.
    /// </summary>
    /// <returns>True when at least one new grant row was inserted.</returns>
    public static async Task<bool> EnsurePermissionsAssignedToRoleAsync(
        AppDbContext context,
        string roleId,
        string grantedByUserId,
        IEnumerable<string> permissionNames,
        CancellationToken cancellationToken = default)
    {
        var names = permissionNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (names.Count == 0)
            return false;

        var changed = false;
        var perms = await context.Permissions
            .Where(p => p.Name != null && names.Contains(p.Name))
            .ToListAsync(cancellationToken);
        var existingRolePerms = await context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(cancellationToken);

        foreach (var perm in perms)
        {
            if (!perm.IsActive)
                continue;

            var matches = existingRolePerms.Where(rp => rp.PermissionId == perm.Id).ToList();
            if (matches.Any(rp => rp.IsActive))
                continue;

            // A prior administrative revoke must not be reversed by seed.
            if (matches.Count > 0)
                continue;

            context.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = perm.Id,
                GrantedByUserId = grantedByUserId,
                GrantedAt = DateTimeOffset.UtcNow,
                IsActive = true
            });
            changed = true;
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);

        return changed;
    }

    /// <summary>
    /// Ensures active direct grants exist for the user. Creates missing active links only.
    /// Does not reactivate an administratively revoked UserPermission.
    /// </summary>
    public static async Task EnsureDirectUserPermissionsAsync(
        AppDbContext context,
        string userId,
        IEnumerable<(string Name, string? Desc)> list,
        CancellationToken cancellationToken = default)
    {
        foreach (var (name, desc) in list)
        {
            var perm = await EnsurePermissionAsync(context, name, desc, cancellationToken);
            if (!perm.IsActive)
                continue;

            var existingGrants = await context.UserPermissions
                .Where(up => up.UserId == userId && up.PermissionId == perm.Id && up.CenterId == null)
                .OrderByDescending(up => up.IsActive)
                .ThenByDescending(up => up.RevokedAt)
                .ToListAsync(cancellationToken);
            if (existingGrants.Any(up => up.IsActive))
                continue;

            if (existingGrants.Count > 0)
                continue;

            context.UserPermissions.Add(new UserPermission
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PermissionId = perm.Id,
                GrantedByUserId = userId,
                GrantedAt = DateTimeOffset.UtcNow,
                IsActive = true
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Renames or consolidates a permission key while moving active assignments (intentional migration).
    /// </summary>
    public static async Task MigratePermissionNameAsync(
        AppDbContext context,
        string oldName,
        string newName,
        string? newDesc,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        var oldPerm = await context.Permissions.FirstOrDefaultAsync(p => p.Name == oldName, cancellationToken);
        if (oldPerm == null)
            return;

        var newPerm = await context.Permissions.FirstOrDefaultAsync(p => p.Name == newName, cancellationToken);
        if (newPerm == null)
        {
            oldPerm.Name = newName;
            if (!string.IsNullOrWhiteSpace(newDesc))
                oldPerm.Description = newDesc;
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var oldRoleLinks = await context.RolePermissions
            .Where(rp => rp.PermissionId == oldPerm.Id)
            .ToListAsync(cancellationToken);
        var newRoleLinks = await context.RolePermissions
            .Where(rp => rp.PermissionId == newPerm.Id)
            .ToListAsync(cancellationToken);
        foreach (var link in oldRoleLinks.Where(link => link.IsActive))
        {
            var counterpart = newRoleLinks.FirstOrDefault(row => row.RoleId == link.RoleId);
            if (counterpart == null)
                link.PermissionId = newPerm.Id;
            else if (!counterpart.IsActive)
            {
                IamRetention.Reactivate(counterpart, actorId, DateTimeOffset.UtcNow);
                IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
            }
            else
                IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
        }

        var oldUserLinks = await context.UserPermissions
            .Where(up => up.PermissionId == oldPerm.Id)
            .ToListAsync(cancellationToken);
        var newUserLinks = await context.UserPermissions
            .Where(up => up.PermissionId == newPerm.Id)
            .ToListAsync(cancellationToken);
        foreach (var link in oldUserLinks.Where(link => link.IsActive))
        {
            var counterpart = newUserLinks.FirstOrDefault(row => row.UserId == link.UserId && row.CenterId == link.CenterId);
            if (counterpart == null)
                link.PermissionId = newPerm.Id;
            else if (!counterpart.IsActive)
            {
                IamRetention.Reactivate(counterpart, actorId, DateTimeOffset.UtcNow);
                IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
            }
            else
                IamRetention.Revoke(link, actorId, IamRetention.PermissionRenamed);
        }

        IamRetention.DeactivatePermission(oldPerm);
        if (!string.IsNullOrWhiteSpace(newDesc) && string.IsNullOrWhiteSpace(newPerm.Description))
            newPerm.Description = newDesc;

        await context.SaveChangesAsync(cancellationToken);
    }
}
