using Domain.Entities.Auth;

namespace Application.Interfaces.Repositories;

public interface IPermissionRepository
{
    Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Permission?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Permission>> GetAllOrderedAsync(CancellationToken cancellationToken = default);
    Task<List<Permission>> GetWithRolesAsync(string? nameFilter, bool? isActive, CancellationToken cancellationToken = default);
    Task<List<Guid>> GetActiveIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<Guid?> FindActivePermissionIdByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<List<string>> GetPermissionNamesViaRolesAsync(string userId, CancellationToken cancellationToken = default);
    void Add(Permission permission);
    void Update(Permission permission);
    void Remove(Permission permission);
    Task<RolePermission?> FindRolePermissionAsync(string roleId, Guid permissionId, CancellationToken cancellationToken = default);
    void AddRolePermission(RolePermission link);
    void RemoveRolePermission(RolePermission link);
}
