using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Enums;

namespace Infrastructure.Services.AuthServices
{
    public class UserPermissionService : IUserPermissionService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IPermissionRepository _permissionRepository;

        public UserPermissionService(
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IPermissionRepository permissionRepository)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _permissionRepository = permissionRepository;
        }

        public bool HasPermission(string userId, string permissionName)
        {
            var userPermissions = _userRepository.GetDirectPermissionNamesAsync(userId).GetAwaiter().GetResult();
            var rolePermissions = _permissionRepository.GetPermissionNamesViaRolesAsync(userId).GetAwaiter().GetResult();

            var allPermissions = userPermissions.Concat(rolePermissions).ToList();

            return allPermissions.Contains(permissionName);
        }

        public FieldAccessType GetFieldAccess(string userId, string entityName, string fieldName)
        {
            // 🔹 أولاً: صلاحيات الحقول المخصصة للمستخدم (أولوية أعلى)
            var userFieldAccess = _userRepository.GetUserFieldAccessAsync(userId, entityName, fieldName).GetAwaiter().GetResult();

            if (userFieldAccess.HasValue)
                return userFieldAccess.Value;

            // 🔹 ثانياً: صلاحيات الحقول المربوطة بأدوار المستخدم
            var roleIds = _userRepository.GetRoleAssociationsAsync(userId).GetAwaiter().GetResult()
                .Where(ura => ura.RoleId != null)
                .Select(ura => ura.RoleId!)
                .ToList();

            if (roleIds.Count == 0)
                return FieldAccessType.None;

            var roleFieldAccess = _roleRepository.GetRoleFieldAccessAsync(roleIds, entityName, fieldName).GetAwaiter().GetResult();

            return roleFieldAccess ?? FieldAccessType.None;
        }
    }
}
