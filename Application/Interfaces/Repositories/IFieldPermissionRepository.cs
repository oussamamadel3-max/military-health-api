using Domain.Entities.Auth;

namespace Application.Interfaces.Repositories;

public interface IFieldPermissionRepository
{
    IQueryable<FieldPermission> Query(bool asNoTracking = false);
    Task<FieldPermission?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<string>> GetExistingFieldNamesAsync(string entityName, CancellationToken cancellationToken = default);
    void Add(FieldPermission entity);
    void Update(FieldPermission entity);
    void Remove(FieldPermission entity);
    Task<List<RoleFieldPermission>> GetRoleLinksAsync(Guid fieldPermissionId, CancellationToken cancellationToken = default);
    void AddRoleLink(RoleFieldPermission link);
    void RemoveRoleLinks(IEnumerable<RoleFieldPermission> links);
    Task<List<UserFieldPermission>> GetUserLinksAsync(Guid fieldPermissionId, CancellationToken cancellationToken = default);
    void AddUserLink(UserFieldPermission link);
    void RemoveUserLinks(IEnumerable<UserFieldPermission> links);
}
