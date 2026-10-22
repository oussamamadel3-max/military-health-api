using Application.Common.Security;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Enums;

namespace Application.Services.AuthServices
{
    public class UserPermissionService : IUserPermissionService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IAccessContextRepository _access;
        private readonly ICurrentCenterService _currentCenter;

        public UserPermissionService(
            IUserRepository userRepository,
            IRoleRepository roleRepository,
            IAccessContextRepository access,
            ICurrentCenterService currentCenter)
        {
            _userRepository = userRepository;
            _roleRepository = roleRepository;
            _access = access;
            _currentCenter = currentCenter;
        }

        public bool HasPermission(string userId, string permissionName)
            => GetPermissionNames(userId).Contains(permissionName, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> GetPermissionNames(string userId)
        {
            var snapshot = _access.LoadAsync(userId).GetAwaiter().GetResult();
            var centerId = _currentCenter.IsCentralContext ? null : _currentCenter.CenterId;
            return CenterPermissionEvaluator.Resolve(snapshot.Roles, snapshot.Directs, snapshot.Links, centerId);
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
