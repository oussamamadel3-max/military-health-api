using Domain.Entities.Auth;

namespace Application.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(string id, bool asNoTracking = false, CancellationToken cancellationToken = default);
    Task<Role?> GetByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default);
    Task<List<(string Id, string? Name)>> ResolveActiveRolesAsync(IEnumerable<string> idsOrNames, CancellationToken cancellationToken = default);
    Task<List<string>> GetActiveRoleIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default);
    IQueryable<Role> QueryRoles(bool asNoTracking = true);
    void Update(Role role);
    Task<List<RolePermission>> GetRolePermissionsAsync(string roleId, CancellationToken cancellationToken = default);
    Task<List<RolePermission>> GetRolePermissionsForRolesAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default);
    Task<List<Guid>> GetActivePermissionIdsForRolesAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default);
    Task<List<Guid>> GetFieldPermissionIdsForRolesAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default);
    Task<List<(string EntityName, string FieldName, Domain.Enums.FieldAccessType AccessType)>> GetRoleFieldPermissionDetailsAsync(IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default);
    Task<Domain.Enums.FieldAccessType?> GetRoleFieldAccessAsync(IEnumerable<string> roleIds, string entityName, string fieldName, CancellationToken cancellationToken = default);
    Task<RolePermission?> FindRolePermissionAsync(string roleId, Guid permissionId, CancellationToken cancellationToken = default);
    void AddRolePermissions(IEnumerable<RolePermission> links);
    void RemoveRolePermissions(IEnumerable<RolePermission> links);
    Task<Dictionary<string, int>> GetAssignedUserCountsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default);
}
