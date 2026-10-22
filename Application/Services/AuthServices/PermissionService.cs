using Application.Common.Security;
using Application.Common.Localization;
using Application.Common.Permissions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Domain.Entities.Auth;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;

namespace Application.Services.AuthServices
{
    public class PermissionService : IPermissionService
    {
        private readonly IPermissionRepository _permissionRepository;
        private readonly IRoleRepository _roleRepository;
        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly RoleManager<Role> _roleManager;
        private readonly UserManager<User> _userManager;
        private readonly IPermissionGate _permissionGate;
        private readonly ICurrentUserService _currentUserService;
        private readonly IMapper _mapper;

        public PermissionService(
            IPermissionRepository permissionRepository,
            IRoleRepository roleRepository,
            IUserRepository userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork,
            RoleManager<Role> roleManager,
            UserManager<User> userManager,
            IPermissionGate permissionGate,
            ICurrentUserService currentUserService,
            IMapper mapper)
        {
            _permissionRepository = permissionRepository;
            _roleRepository = roleRepository;
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _roleManager = roleManager;
            _userManager = userManager;
            _permissionGate = permissionGate;
            _currentUserService = currentUserService;
            _mapper = mapper;
        }

        #region جلب حسب المعرف
        public async Task<PermissionDto> GetByIdAsync(string id)
        {
            if (!Guid.TryParse(id, out var permissionId))
                throw new RoleNotFoundException("الصلاحية غير موجودة.");

            var permission = await _permissionRepository.FindByIdAsync(permissionId)
                ?? throw new RoleNotFoundException("الصلاحية غير موجودة.");

            return _mapper.Map<PermissionDto>(permission);
        }
        #endregion

        #region جلب الكل
        public async Task<List<PermissionDto>> GetPermissionsAsync()
        {
            var permissions = await _permissionRepository.GetAllOrderedAsync();
            return _mapper.Map<List<PermissionDto>>(permissions);
        }

        public async Task<List<PermissionModuleDto>> GetPermissionModulesAsync()
        {
            var permissions = await _permissionRepository.GetAllOrderedAsync();

            var modules = permissions
                .GroupBy(p => PermissionDependencyRules.GetModuleKey(p.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var moduleKey = g.Key;
                    var moduleLabel = AuthDisplayNames.ModuleAr(moduleKey);
                    var items = g
                        .Select(p =>
                        {
                            var name = p.Name ?? string.Empty;
                            var actionKey = PermissionDependencyRules.GetActionKey(name);
                            return new PermissionModuleItemDto
                            {
                                Id = p.Id,
                                Name = name,
                                Description = string.IsNullOrWhiteSpace(p.Description)
                                    ? AuthDisplayNames.PermissionAr(name)
                                    : p.Description,
                                ActionKey = actionKey,
                                ActionLabel = AuthDisplayNames.ActionAr(actionKey),
                                IsActive = p.IsActive,
                            };
                        })
                        .OrderBy(i => i.ActionLabel, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new PermissionModuleDto
                    {
                        ModuleKey = moduleKey,
                        ModuleLabel = moduleLabel,
                        Permissions = items,
                    };
                })
                .OrderBy(m => m.ModuleLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return modules;
        }
        #endregion

        #region جلب الصلاحيات مع الادوار التي ترتبط بها
        public async Task<List<PermissionWithRolesResultDto>> GetPermissionsWithRolesAsync(string? name = null, bool? isActive = null)
        {
            var nameFilter = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            var permissions = await _permissionRepository.GetWithRolesAsync(nameFilter, isActive);
            permissions = permissions.OrderBy(p => p.Name).ThenBy(p => p.Id).ToList();

            return _mapper.Map<List<PermissionWithRolesResultDto>>(permissions);
        }
        #endregion

        #region تعين او الغاء تعين صلاحية لدور معين
        public async Task TogglePermissionForRoleAsync(string roleId, Guid permissionId)
        {
            _permissionGate.EnsureAll(SystemPermissions.RoleManagement.AssignPermissions);

            _ = await _roleManager.FindByIdAsync(roleId)
                ?? throw new RoleNotFoundException("الدور غير موجود.");

            var permission = await _permissionRepository.FindByIdAsync(permissionId)
                ?? throw new RoleNotFoundException("الصلاحية غير موجودة.");

            var existing = await _permissionRepository.FindRolePermissionAsync(roleId, permissionId);
            var previousNames = (await _roleRepository.GetRolePermissionsForRolesAsync(new[] { roleId }))
                .Select(rp => rp.Permission?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList();

            if (existing is { IsActive: true })
            {
                var afterNames = previousNames
                    .Where(n => !string.Equals(n, permission.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                await IamAdminGuard.EnsureAtLeastOneIamAdminRemainsAfterRolePermissionChangeAsync(
                    _userRepository, _roleRepository, roleId, afterNames, previousNames);

                IamRetention.Revoke(existing, AssignmentStamp.RequireActor(_currentUserService.UserId), IamRetention.RolePermissionWithdrawn);
                await _unitOfWork.SaveChangesAsync();
                await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                    _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, roleId);
                return;
            }

            if (!permission.IsActive)
                throw new InvalidRoleDataException("لا يمكن ربط صلاحية غير نشطة بالدور.");

            IamAdminGuard.EnsureAssignableWithinActorPermissions(
                new[] { permission.Name }, _permissionGate.GetPermissions());

            var afterGrant = previousNames
                .Append(permission.Name ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            PermissionDependencyRules.EnsureReadBeforeWriteActions(afterGrant);

            if (existing != null)
            {
                IamRetention.Reactivate(existing, AssignmentStamp.RequireActor(_currentUserService.UserId), DateTimeOffset.UtcNow);
                await _unitOfWork.SaveChangesAsync();
                await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                    _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, roleId);
                return;
            }

            var rolePermission = new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId,
                GrantedByUserId = AssignmentStamp.RequireActor(_currentUserService.UserId),
                GrantedAt = DateTimeOffset.UtcNow,
                IsActive = true
            };

            _permissionRepository.AddRolePermission(rolePermission);
            await _unitOfWork.SaveChangesAsync();

            await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, roleId);
        }

        public async Task TogglePermissionStatusAsync(Guid permissionId)
        {
            _permissionGate.EnsureAll(SystemPermissions.PermissionManagement.ChangeStatus);

            var permission = await _permissionRepository.FindByIdAsync(permissionId)
                ?? throw new RoleNotFoundException("الصلاحية غير موجودة.");

            permission.IsActive = !permission.IsActive;
            _permissionRepository.Update(permission);
            await _unitOfWork.SaveChangesAsync();

            await SecurityStampHelper.InvalidateUsersWithPermissionAsync(
                _permissionRepository, _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, permissionId);
        }
        #endregion
    }
}
