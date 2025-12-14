using ArchiveProject.Domain.Enums;

namespace ArchiveProject.Application.Common.Interfaces.AuthInterfaces
{
    public interface IUserPermissionService
    {

        bool HasPermission(string userId, string permissionName);
        FieldAccessType GetFieldAccess(string userId, string entityName, string fieldName);

    }
}

