using Domain.Enums;

namespace Application.Interfaces.Services
{
    public interface IUserPermissionService
    {

        bool HasPermission(string userId, string permissionName);
        FieldAccessType GetFieldAccess(string userId, string entityName, string fieldName);

    }
}

