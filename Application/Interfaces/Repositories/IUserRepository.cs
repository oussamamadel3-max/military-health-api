using Domain.Entities.Auth;

namespace Application.Interfaces.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, bool asNoTracking = false, CancellationToken cancellationToken = default);
    Task<User?> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    IQueryable<User> QueryUsers(bool asNoTracking = true);
    /// <summary>
    /// مستخدمو المراكز المشتركة مع الممثل، باستثناء من لديهم سلطة مركزية.
    /// </summary>
    IQueryable<User> QueryUsersManageableByLocalActor(string actorUserId, bool asNoTracking = true);
    Task<bool> SharesActiveCenterAsync(string actorUserId, string targetUserId, CancellationToken cancellationToken = default);
    IQueryable<UserRoleAssociation> QueryRoleAssociations(bool asNoTracking = true);
    IQueryable<UserPermission> QueryUserPermissions(bool asNoTracking = true);
    Task<Dictionary<string, string>> GetDisplayNamesByIdsAsync(IEnumerable<string> userIds, CancellationToken cancellationToken = default);
    Task<List<UserRoleAssociation>> GetRoleAssociationsAsync(string userId, bool asNoTracking = false, CancellationToken cancellationToken = default);
    Task<List<string>> GetActiveRoleNamesAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<string>> GetActiveRoleIdsAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<string>> GetDirectPermissionNamesAsync(string userId, bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<List<(string EntityName, string FieldName, Domain.Enums.FieldAccessType AccessType)>> GetUserFieldPermissionDetailsAsync(string userId, CancellationToken cancellationToken = default);
    Task<Domain.Enums.FieldAccessType?> GetUserFieldAccessAsync(string userId, string entityName, string fieldName, CancellationToken cancellationToken = default);
    void AddRoleAssociations(IEnumerable<UserRoleAssociation> associations);
    void RemoveRoleAssociations(IEnumerable<UserRoleAssociation> associations);
    void AddUserPermissions(IEnumerable<UserPermission> permissions);
    void RemoveUserPermissions(IEnumerable<UserPermission> permissions);
    void AddUserFieldPermissions(IEnumerable<UserFieldPermission> permissions);
    void RemoveUserFieldPermissions(IEnumerable<UserFieldPermission> permissions);
    Task<List<UserPermission>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<UserFieldPermission>> GetUserFieldPermissionsAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<Guid>> GetDirectPermissionIdsAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<Guid>> GetDirectFieldPermissionIdsAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<string>> GetUserIdsByRoleIdAsync(string roleId, CancellationToken cancellationToken = default);
    Task<List<string>> GetActiveUserIdsExceptAsync(string excludeUserId, CancellationToken cancellationToken = default);
}
