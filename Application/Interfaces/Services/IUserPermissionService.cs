using Domain.Enums;

namespace Application.Interfaces.Services
{
    public interface IUserPermissionService
    {

        bool HasPermission(string userId, string permissionName);
        IReadOnlyList<string> GetPermissionNames(string userId);
        FieldAccessType GetFieldAccess(string userId, string entityName, string fieldName);

    }
}

