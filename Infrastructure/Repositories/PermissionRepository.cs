using Application.Common.Security;
using Application.Interfaces.Repositories;
using Domain.Entities.Auth;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class PermissionRepository : IPermissionRepository
{
    private readonly AppDbContext _context;

    public PermissionRepository(AppDbContext context) => _context = context;

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
        => await _context.Permissions.AnyAsync(p => p.Name == name, cancellationToken);

    public Task<Permission?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Permissions.FindAsync([id], cancellationToken).AsTask();

    public async Task<List<Permission>> GetAllOrderedAsync(CancellationToken cancellationToken = default)
        => await _context.Permissions.OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<List<Permission>> GetWithRolesAsync(string? nameFilter, bool? isActive, CancellationToken cancellationToken = default)
    {
        var query = _context.Permissions.AsNoTracking().Include(p => p.RolePermissions).ThenInclude(rp => rp.Role).AsQueryable();
        if (!string.IsNullOrWhiteSpace(nameFilter))
            query = query.Where(p => p.Name.Contains(nameFilter));
        if (isActive.HasValue)
            query = query.Where(p => p.IsActive == isActive.Value);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetActiveIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        return await _context.Permissions.AsNoTracking()
            .Where(p => idList.Contains(p.Id) && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid?> FindActivePermissionIdByNameAsync(string name, CancellationToken cancellationToken = default) =>
        await _context.Permissions.AsNoTracking()
            .Where(p => p.Name == name && p.IsActive)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
    public async Task<List<Permission>> GetActiveByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return new List<Permission>();

        return await _context.Permissions.AsNoTracking()
            .Where(p => idList.Contains(p.Id) && p.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetPermissionNamesViaRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserRoleAssociations
            .Where(ura => ura.UserId == userId && ura.IsActive && ura.Role != null && ura.Role.IsActive)
            .SelectMany(ura => ura.Role!.RolePermissions)
            .Where(rp => rp.IsActive && rp.Permission != null && rp.Permission.IsActive)
            .Select(rp => rp.Permission!.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public void Add(Permission permission) => _context.Permissions.Add(permission);
    public void Update(Permission permission) => _context.Permissions.Update(permission);
    public void Remove(Permission permission)
        => throw new InvalidOperationException("Physical delete of permissions is disabled. Deactivate the permission.");

    public async Task<RolePermission?> FindRolePermissionAsync(string roleId, Guid permissionId, CancellationToken cancellationToken = default)
        => await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId && rp.PermissionId == permissionId)
            .OrderByDescending(rp => rp.IsActive)
            .ThenByDescending(rp => rp.RevokedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public void AddRolePermission(RolePermission link)
    {
        var existing = _context.RolePermissions
            .Where(x => x.RoleId == link.RoleId && x.PermissionId == link.PermissionId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.RevokedAt)
            .FirstOrDefault();
        if (existing is { IsActive: true })
            return;
        if (existing != null)
        {
            IamRetention.Reactivate(existing, link.GrantedByUserId, link.GrantedAt);
            return;
        }

        _context.RolePermissions.Add(link);
    }

    public void RemoveRolePermission(RolePermission link)
        => throw new InvalidOperationException("Physical delete of role permissions is disabled. Revoke the assignment.");

    public async Task<List<string>> GetRoleIdsByPermissionIdAsync(Guid permissionId, CancellationToken cancellationToken = default)
        => await _context.RolePermissions.AsNoTracking()
            .Where(rp => rp.PermissionId == permissionId && rp.IsActive)
            .Select(rp => rp.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<List<string>> GetUserIdsByPermissionIdAsync(Guid permissionId, CancellationToken cancellationToken = default)
        => await _context.UserPermissions.AsNoTracking()
            .Where(up => up.PermissionId == permissionId && up.IsActive)
            .Select(up => up.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
}
