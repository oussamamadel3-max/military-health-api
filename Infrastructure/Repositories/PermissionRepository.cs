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

    public async Task<List<string>> GetPermissionNamesViaRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserRoleAssociations
            .Where(ura => ura.UserId == userId)
            .SelectMany(ura => ura.Role!.RolePermissions)
            .Where(rp => rp.Permission != null)
            .Select(rp => rp.Permission!.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public void Add(Permission permission) => _context.Permissions.Add(permission);
    public void Update(Permission permission) => _context.Permissions.Update(permission);
    public void Remove(Permission permission) => _context.Permissions.Remove(permission);

    public async Task<RolePermission?> FindRolePermissionAsync(string roleId, Guid permissionId, CancellationToken cancellationToken = default)
        => await _context.RolePermissions.FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId, cancellationToken);

    public void AddRolePermission(RolePermission link) => _context.RolePermissions.Add(link);
    public void RemoveRolePermission(RolePermission link) => _context.RolePermissions.Remove(link);
}
