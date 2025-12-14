using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Domain.Enums;
using ArchiveProject.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;

namespace ArchiveProject.Infrastructure.Services.AuthServices
{
    public class UserPermissionService : IUserPermissionService
    {
        private readonly MainDbContext _context;
        public UserPermissionService(MainDbContext context, UserManager<User> userManager)
        {
            _context = context;
        }

        public bool HasPermission(string userId, string permissionName)
        {
            var userPermissions = _context.UserPermissions
                .Where(up => up.UserId == userId)
                .Select(up => up.Permission.Name!)
                .ToList();

            var rolePermissions = _context.UserRoleAssociations
                .Where(ura => ura.UserId == userId)
                .SelectMany(ura => ura.Role.RolePermissions)
                .Select(rp => rp.Permission.Name!)
                .ToList();

            var allPermissions = userPermissions.Concat(rolePermissions).ToList();

            return allPermissions.Contains(permissionName);
        }

        public FieldAccessType GetFieldAccess(string userId, string entityName, string fieldName)
        {
            // 🔹 أولاً: صلاحيات الحقول المخصصة للمستخدم (أولوية أعلى)
            var userFieldAccess = _context.UserFieldPermissions
                .Where(ufp => ufp.UserId == userId &&
                              ufp.FieldPermission.EntityName == entityName &&
                              ufp.FieldPermission.FieldName == fieldName)
                .Select(ufp => (FieldAccessType?)ufp.FieldPermission.AccessType)
                .FirstOrDefault();

            if (userFieldAccess.HasValue)
                return userFieldAccess.Value;

            // 🔹 ثانياً: صلاحيات الحقول المربوطة بأدوار المستخدم
            var roleIds = _context.UserRoleAssociations
                .Where(ura => ura.UserId == userId && ura.RoleId != null)
                .Select(ura => ura.RoleId!)
                .ToList();

            if (roleIds.Count == 0)
                return FieldAccessType.None;

            var roleFieldAccess = _context.RoleFieldPermissions
                .Where(rfp => roleIds.Contains(rfp.RoleId) &&
                              rfp.FieldPermission.EntityName == entityName &&
                              rfp.FieldPermission.FieldName == fieldName)
                .Select(rfp => (FieldAccessType?)rfp.FieldPermission.AccessType)
                .FirstOrDefault();

            return roleFieldAccess ?? FieldAccessType.None;
        }


    }
}

