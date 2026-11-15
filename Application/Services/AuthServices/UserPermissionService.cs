using Application.Common.Permissions;
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
            var centerId = UsableCenterId(userId);
            return CenterPermissionEvaluator.Resolve(snapshot.Roles, snapshot.Directs, snapshot.Links, centerId);
        }

        private Guid? UsableCenterId(string userId)
        {
            if (_currentCenter.IsCentralContext || _currentCenter.CenterId is not Guid claimed)
                return null;

            return ActiveCenterSessionGate.ResolveClaimedCenter(
                claimed,
                _access.IsActivelyAssignedAsync(userId, claimed).GetAwaiter().GetResult());
        }

        public FieldAccessType GetFieldAccess(string userId, string entityName, string fieldName)
            => GetFieldAccessAsync(userId, entityName, fieldName).GetAwaiter().GetResult();

        public async Task<FieldAccessType> GetFieldAccessAsync(
            string userId,
            string entityName,
            string fieldName,
            CancellationToken cancellationToken = default)
        {
            var userFieldAccess = await _userRepository.GetUserFieldAccessAsync(
                userId, entityName, fieldName, cancellationToken);
            var roleIds = await _userRepository.GetActiveRoleIdsAsync(userId, cancellationToken);
            IReadOnlyList<FieldAccessType> roleAccessTypes = roleIds.Count == 0
                ? Array.Empty<FieldAccessType>()
                : await _roleRepository.GetRoleFieldAccessTypesAsync(
                    roleIds, entityName, fieldName, cancellationToken);

            return FieldAccessSemantics.ResolveEffective(userFieldAccess, roleAccessTypes);
        }
    }
}
