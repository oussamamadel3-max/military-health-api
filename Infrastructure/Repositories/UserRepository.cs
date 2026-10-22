using Application.Common.Security;
using Application.Interfaces.Repositories;
using Domain.Entities.Auth;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context) => _context = context;

    public async Task<User?> GetByIdAsync(string id, bool asNoTracking = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Users.AsQueryable();
        if (asNoTracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public Task<User?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        => _context.Users.FindAsync([id], cancellationToken).AsTask();

    public IQueryable<User> QueryUsers(bool asNoTracking = true)
        => asNoTracking ? _context.Users.AsNoTracking() : _context.Users;

    public IQueryable<UserRoleAssociation> QueryRoleAssociations(bool asNoTracking = true)
        => asNoTracking ? _context.UserRoleAssociations.AsNoTracking() : _context.UserRoleAssociations;

    public IQueryable<UserPermission> QueryUserPermissions(bool asNoTracking = true)
        => asNoTracking ? _context.UserPermissions.AsNoTracking() : _context.UserPermissions;

    public async Task<Dictionary<string, string>> GetDisplayNamesByIdsAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        return await _context.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? u.Id, cancellationToken);
    }

    public async Task<List<UserRoleAssociation>> GetRoleAssociationsAsync(string userId, bool asNoTracking = false, CancellationToken cancellationToken = default)
    {
        var query = _context.UserRoleAssociations.Where(ura => ura.UserId == userId);
        if (asNoTracking) query = query.AsNoTracking();
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetActiveRoleNamesAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserRoleAssociations
            .Where(ura => ura.UserId == userId && ura.IsActive && ura.Role != null && ura.Role.IsActive)
            .Select(ura => ura.Role!.Name!)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetActiveRoleIdsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserRoleAssociations
            .Where(ura => ura.UserId == userId && ura.IsActive && ura.Role != null && ura.Role.IsActive)
            .Select(ura => ura.RoleId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetDirectPermissionNamesAsync(string userId, bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = _context.UserPermissions.Where(up => up.UserId == userId && up.IsActive);
        if (activeOnly) query = query.Where(up => up.Permission != null && up.Permission.IsActive);
        return await query
            .Select(up => up.Permission!.Name)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<(string EntityName, string FieldName, Domain.Enums.FieldAccessType AccessType)>> GetUserFieldPermissionDetailsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserFieldPermissions
            .Where(ufp => ufp.UserId == userId)
            .Select(ufp => new ValueTuple<string, string, Domain.Enums.FieldAccessType>(
                ufp.FieldPermission.EntityName,
                ufp.FieldPermission.FieldName,
                ufp.FieldPermission.AccessType))
            .ToListAsync(cancellationToken);
    }

    public async Task<Domain.Enums.FieldAccessType?> GetUserFieldAccessAsync(string userId, string entityName, string fieldName, CancellationToken cancellationToken = default)
    {
        return await _context.UserFieldPermissions
            .Where(ufp => ufp.UserId == userId
                && ufp.FieldPermission.EntityName == entityName
                && ufp.FieldPermission.FieldName == fieldName)
            .Select(ufp => (Domain.Enums.FieldAccessType?)ufp.FieldPermission.AccessType)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void AddRoleAssociations(IEnumerable<UserRoleAssociation> associations)
    {
        foreach (var incoming in associations)
        {
            var existing = _context.UserRoleAssociations
                .Where(x => x.UserId == incoming.UserId && x.RoleId == incoming.RoleId && x.CenterId == incoming.CenterId)
                .OrderByDescending(x => x.IsActive)
                .ThenByDescending(x => x.RevokedAt)
                .FirstOrDefault();
            if (existing is { IsActive: true })
                continue;
            if (existing != null)
            {
                IamRetention.Reactivate(existing, incoming.AssignedByUserId, incoming.AssignedAt);
                continue;
            }

            _context.UserRoleAssociations.Add(incoming);
        }
    }

    public void RemoveRoleAssociations(IEnumerable<UserRoleAssociation> associations)
        => throw new InvalidOperationException("Physical delete of user role assignments is disabled. Revoke the assignment.");

    public void AddUserPermissions(IEnumerable<UserPermission> permissions)
    {
        foreach (var incoming in permissions)
        {
            var existing = _context.UserPermissions
                .Where(x => x.UserId == incoming.UserId && x.PermissionId == incoming.PermissionId && x.CenterId == incoming.CenterId)
                .OrderByDescending(x => x.IsActive)
                .ThenByDescending(x => x.RevokedAt)
                .FirstOrDefault();
            if (existing is { IsActive: true })
                continue;
            if (existing != null)
            {
                IamRetention.Reactivate(existing, incoming.GrantedByUserId, incoming.GrantedAt);
                continue;
            }

            _context.UserPermissions.Add(incoming);
        }
    }

    public void RemoveUserPermissions(IEnumerable<UserPermission> permissions)
        => throw new InvalidOperationException("Physical delete of direct user permissions is disabled. Revoke the assignment.");

    public void AddUserFieldPermissions(IEnumerable<UserFieldPermission> permissions)
        => _context.UserFieldPermissions.AddRange(permissions);

    public void RemoveUserFieldPermissions(IEnumerable<UserFieldPermission> permissions)
        => _context.UserFieldPermissions.RemoveRange(permissions);

    public async Task<List<UserPermission>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default)
        => await _context.UserPermissions.Where(up => up.UserId == userId).ToListAsync(cancellationToken);

    public async Task<List<UserFieldPermission>> GetUserFieldPermissionsAsync(string userId, CancellationToken cancellationToken = default)
        => await _context.UserFieldPermissions.Where(ufp => ufp.UserId == userId).ToListAsync(cancellationToken);

    public async Task<List<Guid>> GetDirectPermissionIdsAsync(string userId, CancellationToken cancellationToken = default)
        => await _context.UserPermissions
            .Where(up => up.UserId == userId && up.IsActive)
            .Select(up => up.PermissionId)
            .ToListAsync(cancellationToken);

    public async Task<List<Guid>> GetDirectFieldPermissionIdsAsync(string userId, CancellationToken cancellationToken = default)
        => await _context.UserFieldPermissions.Where(ufp => ufp.UserId == userId).Select(ufp => ufp.FieldPermissionId).ToListAsync(cancellationToken);

    public async Task<List<string>> GetUserIdsByRoleIdAsync(string roleId, CancellationToken cancellationToken = default)
        => await _context.UserRoleAssociations
            .Where(ura => ura.RoleId == roleId && ura.IsActive)
            .Select(ura => ura.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<List<string>> GetActiveUserIdsExceptAsync(string excludeUserId, CancellationToken cancellationToken = default)
        => await _context.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != excludeUserId)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
}
