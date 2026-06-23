using Application.Interfaces.Repositories;
using Domain.Entities.Auth;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _context;

    public RoleRepository(AppDbContext context) => _context = context;

    public async Task<Role?> GetByIdAsync(string id, bool asNoTracking = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Roles.AsQueryable();
        if (asNoTracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<Role?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default)
        => await _context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.NormalizedName == normalizedName, cancellationToken);

    public async Task<string?> GetSuperAdminRoleIdAsync(CancellationToken cancellationToken = default)
        => await _context.Roles.AsNoTracking().Where(r => r.NormalizedName == "SUPERADMIN").Select(r => r.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<List<(string Id, string? Name)>> ResolveActiveRolesAsync(IEnumerable<string> idsOrNames, CancellationToken cancellationToken = default)
    {
        var rawUpper = idsOrNames.Select(x => x.Trim().ToUpperInvariant()).ToHashSet();
        return await _context.Roles
            .Where(r => (r.Id != null && rawUpper.Contains(r.Id.ToUpper())) || (r.Name != null && rawUpper.Contains(r.Name.ToUpper())))
            .Where(r => r.IsActive)
            .Select(r => new ValueTuple<string, string?>(r.Id, r.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetActiveRoleIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        var ids = roleIds.ToList();
        return await _context.Roles.AsNoTracking()
            .Where(r => ids.Contains(r.Id) && r.IsActive)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public IQueryable<Role> QueryRoles(bool asNoTracking = true)
        => asNoTracking ? _context.Roles.AsNoTracking() : _context.Roles;

    public void Update(Role role) => _context.Roles.Update(role);

    public async Task<List<RolePermission>> GetRolePermissionsAsync(string roleId, CancellationToken cancellationToken = default)
        => await _context.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(cancellationToken);

    public async Task<List<RolePermission>> GetRolePermissionsForRolesAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        var ids = roleIds.ToList();
        return await _context.RolePermissions
            .Where(rp => ids.Contains(rp.RoleId))
            .Include(rp => rp.Permission)
            .Where(rp => rp.Permission != null && rp.Permission.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetActivePermissionIdsForRolesAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        var ids = roleIds.ToList();
        return await _context.RolePermissions
            .Where(rp => ids.Contains(rp.RoleId) && rp.Permission != null && rp.Permission.IsActive)
            .Select(rp => rp.PermissionId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetFieldPermissionIdsForRolesAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        var ids = roleIds.ToList();
        return await _context.RoleFieldPermissions
            .Where(rfp => ids.Contains(rfp.RoleId))
            .Select(rfp => rfp.FieldPermissionId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<(string EntityName, string FieldName, Domain.Enums.FieldAccessType AccessType)>> GetRoleFieldPermissionDetailsAsync(IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default)
    {
        return await _context.RoleFieldPermissions
            .Where(rfp => rfp.Role != null && rfp.Role.Name != null && roleNames.Contains(rfp.Role.Name))
            .Select(rfp => new ValueTuple<string, string, Domain.Enums.FieldAccessType>(
                rfp.FieldPermission.EntityName,
                rfp.FieldPermission.FieldName,
                rfp.FieldPermission.AccessType))
            .ToListAsync(cancellationToken);
    }

    public async Task<Domain.Enums.FieldAccessType?> GetRoleFieldAccessAsync(IEnumerable<string> roleIds, string entityName, string fieldName, CancellationToken cancellationToken = default)
    {
        var ids = roleIds.ToList();
        return await _context.RoleFieldPermissions
            .Where(rfp => ids.Contains(rfp.RoleId)
                && rfp.FieldPermission.EntityName == entityName
                && rfp.FieldPermission.FieldName == fieldName)
            .Select(rfp => (Domain.Enums.FieldAccessType?)rfp.FieldPermission.AccessType)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<RolePermission?> FindRolePermissionAsync(string roleId, Guid permissionId, CancellationToken cancellationToken = default)
        => await _context.RolePermissions.FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId, cancellationToken);

    public void AddRolePermissions(IEnumerable<RolePermission> links) => _context.RolePermissions.AddRange(links);
    public void RemoveRolePermissions(IEnumerable<RolePermission> links) => _context.RolePermissions.RemoveRange(links);

    public async Task<Dictionary<string, int>> GetAssignedUserCountsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        var ids = roleIds.ToList();
        return await _context.UserRoleAssociations
            .Where(ura => ids.Contains(ura.RoleId))
            .GroupBy(ura => ura.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);
    }
}
