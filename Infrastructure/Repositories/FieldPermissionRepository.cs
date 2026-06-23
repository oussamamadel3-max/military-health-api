using Application.Interfaces.Repositories;
using Domain.Entities.Auth;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class FieldPermissionRepository : IFieldPermissionRepository
{
    private readonly AppDbContext _context;

    public FieldPermissionRepository(AppDbContext context) => _context = context;

    public IQueryable<FieldPermission> Query(bool asNoTracking = false)
        => asNoTracking ? _context.FieldPermissions.AsNoTracking() : _context.FieldPermissions;

    public Task<FieldPermission?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.FieldPermissions.FindAsync([id], cancellationToken).AsTask();

    public async Task<List<string>> GetExistingFieldNamesAsync(string entityName, CancellationToken cancellationToken = default)
        => await _context.FieldPermissions.AsNoTracking()
            .Where(fp => fp.EntityName == entityName)
            .Select(fp => fp.FieldName)
            .ToListAsync(cancellationToken);

    public void Add(FieldPermission entity) => _context.FieldPermissions.Add(entity);
    public void Update(FieldPermission entity) => _context.FieldPermissions.Update(entity);
    public void Remove(FieldPermission entity) => _context.FieldPermissions.Remove(entity);

    public async Task<List<RoleFieldPermission>> GetRoleLinksAsync(Guid fieldPermissionId, CancellationToken cancellationToken = default)
        => await _context.RoleFieldPermissions.Where(rfp => rfp.FieldPermissionId == fieldPermissionId).ToListAsync(cancellationToken);

    public void AddRoleLink(RoleFieldPermission link) => _context.RoleFieldPermissions.Add(link);
    public void RemoveRoleLinks(IEnumerable<RoleFieldPermission> links) => _context.RoleFieldPermissions.RemoveRange(links);

    public async Task<List<UserFieldPermission>> GetUserLinksAsync(Guid fieldPermissionId, CancellationToken cancellationToken = default)
        => await _context.UserFieldPermissions.Where(ufp => ufp.FieldPermissionId == fieldPermissionId).ToListAsync(cancellationToken);

    public void AddUserLink(UserFieldPermission link) => _context.UserFieldPermissions.Add(link);
    public void RemoveUserLinks(IEnumerable<UserFieldPermission> links) => _context.UserFieldPermissions.RemoveRange(links);
}
