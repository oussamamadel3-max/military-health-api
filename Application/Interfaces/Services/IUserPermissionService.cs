using Domain.Enums;

namespace Application.Interfaces.Services
{
    public interface IUserPermissionService
    {

        bool HasPermission(string userId, string permissionName);
        IReadOnlyList<string> GetPermissionNames(string userId);
        FieldAccessType GetFieldAccess(string userId, string entityName, string fieldName);

        /// <summary>
        /// نسخة غير متزامنة لـ <see cref="GetFieldAccess"/> لتجنب الحظر المتزامن على نفس DbContext أثناء طلبات الأعمال.
        /// </summary>
        Task<FieldAccessType> GetFieldAccessAsync(
            string userId,
            string entityName,
            string fieldName,
            CancellationToken cancellationToken = default);
    }
}

