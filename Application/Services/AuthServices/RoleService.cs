using MapsterMapper;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Common.Utilities;
using Application.Common.Results;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Domain.Entities.Auth;
using Application.Common.Permissions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Application.Common.Localization;

namespace Application.Services.AuthServices
{
    public class RoleService : IRoleService
    {
        private readonly RoleManager<Role> _roleManager;
        private readonly UserManager<User> _userManager;
        private readonly IRoleRepository _roleRepository;
        private readonly IPermissionRepository _permissionRepository;
        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ICurrentUserService _currentUserService;
        private readonly IPermissionGate _permissionGate;

        public RoleService(
            RoleManager<Role> roleManager,
            IUserRepository userRepository,
            UserManager<User> userManager,
            IMapper mapper,
            ICurrentUserService currentUserService,
            IPermissionGate permissionGate,
            IRoleRepository roleRepository,
            IPermissionRepository permissionRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IUnitOfWork unitOfWork)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _roleRepository = roleRepository;
            _permissionRepository = permissionRepository;
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _currentUserService = currentUserService;
            _permissionGate = permissionGate;
        }

        #region         الانشاء
        public async Task<RoleDto> CreateRoleAsync(CreateRoleDto newRole)
        {
            _permissionGate.EnsureAll(SystemPermissions.RoleManagement.Create);

            var roleName = (newRole.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(roleName))
                throw new InvalidRoleDataException("اسم الدور مطلوب.");

            var roleExists = await _roleManager.RoleExistsAsync(roleName);
            if (roleExists)
                throw new RoleAlreadyExistsException(roleName);

            var role = _mapper.Map<Role>(newRole);

            role.CreatedAt = DateTime.Now;
            role.CreatedBy = _currentUserService.UserId;

            var identityResult = await _roleManager.CreateAsync(role);

            if (!identityResult.Succeeded)
            {
                var errors = identityResult.Errors.Select(e => e.Description);
                throw new InvalidRoleDataException(string.Join(", ", errors));
            }

            return _mapper.Map<RoleDto>(role);
        }
        #endregion


        #region         التعديل
        public async Task UpdateRoleAsync(string id, UpdateRoleDto updatedRole)
        {
            _permissionGate.EnsureAll(SystemPermissions.RoleManagement.Edit);

            var role = await _roleManager.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("الدور غير موجود.");

            var newRoleName = (updatedRole.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newRoleName))
                throw new InvalidRoleDataException("اسم الدور مطلوب.");

            var existingRoleWithSameName = await _roleManager.FindByNameAsync(newRoleName);
            if (existingRoleWithSameName != null && existingRoleWithSameName.Id != role.Id)
                throw new RoleAlreadyExistsException(newRoleName);

            _mapper.Map(updatedRole, role);

            role.UpdatedAt = DateTime.Now;
            role.UpdatedBy = _currentUserService.UserId;

            var identityResult = await _roleManager.UpdateAsync(role);
            if (!identityResult.Succeeded)
            {
                var errors = identityResult.Errors.Select(e => e.Description);
                throw new InvalidRoleDataException(string.Join(", ", errors));
            }

            await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, id);
        }
        #endregion


        #region         الحذف
        public async Task DeleteRoleAsync(string id)
        {
            _permissionGate.EnsureAll(SystemPermissions.RoleManagement.Delete);

            var role = await _roleManager.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("الدور غير موجود.");

            await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, id);

            var result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(e => e.Description);
                throw new InvalidRoleDataException(string.Join(", ", errors));
            }
        }
        #endregion


        #region      جلب حسب المعرف 
        public async Task<RoleDto> GetById(string id)
        {
            var role = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id == id)
                ?? throw new RoleNotFoundException("الدور غير موجود.");

            return _mapper.Map<RoleDto>(role);
        }
        #endregion


        #region          جلب الصلاحيات مع تفاصيلها 
        public async Task<PaginatedList<RoleDetailsDto>> GetRolesWithDetailsAsync(int pageNumber, int pageSize, string? name = null, bool? isActive = null)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            var rolesQuery = _roleManager.Roles.AsQueryable();

            if (!string.IsNullOrWhiteSpace(name))
            {
                var s = SqlLikePatternEscape.EscapeForSqlServerLike(name.Trim());
                rolesQuery = rolesQuery.Where(r => EF.Functions.Like(r.Name!, $"%{s}%"));
            }

            if (isActive.HasValue)
                rolesQuery = rolesQuery.Where(r => r.IsActive == isActive.Value);

            rolesQuery = rolesQuery.OrderBy(r => r.Name);

            var totalRecords = await rolesQuery.CountAsync();

            var roles = await rolesQuery
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            if (roles.Count == 0)
            {
                return new PaginatedList<RoleDetailsDto>(new List<RoleDetailsDto>(), totalRecords, pageNumber, pageSize);
            }

            var roleIds = roles.Select(r => r.Id).ToList();

            var rolePermissions = await _roleRepository.GetRolePermissionsForRolesAsync(roleIds);
            var permissionsDict = rolePermissions
                .Where(rp => rp.Permission != null)
                .GroupBy(rp => rp.RoleId)
                .ToDictionary(
                    g => g.Key,
                    g => _mapper.Map<List<PermissionInfoDto>>(g.Select(rp => rp.Permission!).ToList()));

            var assignedUsersDict = await _roleRepository.GetAssignedUserCountsAsync(roleIds);

            var userIds = roles
                .SelectMany(r => new[] { r.CreatedBy, r.UpdatedBy })
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .Cast<string>()
                .ToList();

            var usersDict = await _userRepository.GetDisplayNamesByIdsAsync(userIds);

            var roleDtos = roles.Select(role =>
            {
                var dto = _mapper.Map<RoleDetailsDto>(role);

                dto.Name = AuthDisplayNames.RoleAr(role.Name);

                permissionsDict.TryGetValue(role.Id, out var perms);
                assignedUsersDict.TryGetValue(role.Id, out var assignedCount);

                dto.Permissions = perms ?? new List<PermissionInfoDto>();
                dto.PermissionsCount = dto.Permissions.Count;
                dto.AssignedUsersCount = assignedCount;

                dto.CreatedByName = !string.IsNullOrEmpty(role.CreatedBy) && usersDict.TryGetValue(role.CreatedBy, out var cb) ? cb : null;
                dto.UpdatedByName = !string.IsNullOrEmpty(role.UpdatedBy) && usersDict.TryGetValue(role.UpdatedBy, out var ub) ? ub : null;

                return dto;
            }).ToList();

            return new PaginatedList<RoleDetailsDto>(roleDtos, totalRecords, pageNumber, pageSize);
        }

        #endregion


        #region         اسناد مستخدم الى دور 
        public async Task AssignUserToRole(string userId, List<string> roleIds)
        {
            var user = await _userManager.FindByIdAsync(userId)
                ?? throw new UserNotFoundException("المستخدم غير موجود.");

            var currentRoles = await _userManager.GetRolesAsync(user);

            var activeRoles = await _roleManager.Roles
                .Where(r => roleIds.Contains(r.Id) && r.IsActive)
                .Select(r => new { r.Id, r.Name })
                .ToListAsync();

            var activeRoleIds = activeRoles.Select(r => r.Id).ToList();
            var inactiveRoleIds = roleIds.Except(activeRoleIds).ToList();

            if (inactiveRoleIds.Any())
                throw new InvalidRoleDataException($"الأدوار التالية غير نشطة: {string.Join(", ", inactiveRoleIds)}");

            var updatedRoleNames = activeRoles
                .Select(r => r.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToList();

            var addedRoles = updatedRoleNames.Except(currentRoles).ToList();

            var removedRoles = currentRoles.Except(updatedRoleNames).ToList();

            if (addedRoles.Any())
            {
                await _userManager.AddToRolesAsync(user, addedRoles);
            }

            if (removedRoles.Any())
            {
                await _userManager.RemoveFromRolesAsync(user, removedRoles);
            }
        }
        #endregion


        #region         اسناد صلاحية الى دور 
        public async Task<string> SyncPermissionsForRoleAsync(AssignPermissionsDto dto)
        {
            _permissionGate.EnsureAll(SystemPermissions.RoleManagement.AssignPermissions);

            if (string.IsNullOrWhiteSpace(dto.RoleId))
                throw new InvalidRoleDataException("RoleId مطلوب.");

            var role = await _roleManager.FindByIdAsync(dto.RoleId)
                ?? throw new RoleNotFoundException("الدور غير موجود.");

            role.UpdatedAt = DateTime.Now;
            role.UpdatedBy = _currentUserService.UserId;

            var requestedIds = (dto.PermissionsIds ?? new List<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            if (requestedIds.Count == 0)
                throw new InvalidRoleDataException("يجب اختيار صلاحية واحدة على الأقل.");

            var activePermissions = await _permissionRepository.GetActiveByIdsAsync(requestedIds);
            var activeValidIds = activePermissions.Select(p => p.Id).ToList();

            var missingOrInactive = requestedIds.Except(activeValidIds).ToList();
            if (missingOrInactive.Any())
                throw new InvalidRoleDataException($"صلاحيات غير موجودة أو غير نشطة: {string.Join(", ", missingOrInactive)}");

            PermissionDependencyRules.EnsureReadBeforeWriteActions(
                activePermissions.Select(p => p.Name));

            IamAdminGuard.EnsureAssignableWithinActorPermissions(
                activePermissions.Select(p => p.Name), _permissionGate.GetPermissions());

            var existingRolePerms = await _roleRepository.GetRolePermissionsAsync(dto.RoleId);

            var existingIds = existingRolePerms
                .Select(rp => rp.PermissionId)
                .ToHashSet();

            var toAdd = activeValidIds
                .Where(id => !existingIds.Contains(id))
                .ToList();

            var toRemove = existingRolePerms
                .Where(rp => !activeValidIds.Contains(rp.PermissionId))
                .ToList();

            using var tx = await _unitOfWork.BeginTransactionAsync();
            try
            {
                if (toRemove.Any())
                    _roleRepository.RemoveRolePermissions(toRemove);

                if (toAdd.Any())
                {
                    var addRows = toAdd.Select(pid => new RolePermission
                    {
                        RoleId = dto.RoleId,
                        PermissionId = pid
                    });
                    _roleRepository.AddRolePermissions(addRows);
                }

                await _unitOfWork.SaveChangesAsync();

                await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                    _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, dto.RoleId);

                await tx.CommitAsync();

                return $"تم تحديث الصلاحيات بنجاح. (إضافة: {toAdd.Count} | حذف: {toRemove.Count})";
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        #endregion


        #region          انشاء دور مع الصلاحيات
        public async Task<RoleDetailsDto> CreateRoleWithPermissionsAsync(CreateRoleWithPermissionsDto dto)
        {
            _permissionGate.EnsureAll(SystemPermissions.RoleManagement.Create, SystemPermissions.RoleManagement.AssignPermissions);

            var roleName = (dto.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(roleName))
                throw new InvalidRoleDataException("اسم الدور مطلوب.");

            var existingRole = await _roleManager.FindByNameAsync(roleName);
            if (existingRole != null)
                throw new RoleAlreadyExistsException(roleName);

            var inputIds = dto.PermissionIds ?? Enumerable.Empty<Guid>();
            var requestedDistinct = inputIds.Distinct().ToList();
            if (requestedDistinct.Count == 0)
                throw new InvalidRoleDataException("يجب اختيار صلاحية واحدة على الأقل.");

            var inputDuplicatesSkipped = inputIds.Count() - requestedDistinct.Count;

            var activePermissions = await _permissionRepository.GetActiveByIdsAsync(requestedDistinct);
            var validIds = activePermissions.Select(p => p.Id).ToList();

            var inactiveIds = requestedDistinct.Except(validIds).ToList();
            if (inactiveIds.Any())
                throw new InvalidRoleDataException($"الصلاحيات التالية غير نشطة: {string.Join(", ", inactiveIds)}");

            PermissionDependencyRules.EnsureReadBeforeWriteActions(
                activePermissions.Select(p => p.Name));

            IamAdminGuard.EnsureAssignableWithinActorPermissions(
                activePermissions.Select(p => p.Name), _permissionGate.GetPermissions());

            var invalidSkipped = requestedDistinct.Count - validIds.Count;

            var role = _mapper.Map<Role>(dto);
            role.Name = roleName;
            role.CreatedAt = DateTime.Now;
            role.CreatedBy = _currentUserService.UserId;
            role.IsActive = true;

            using var tx = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var identityResult = await _roleManager.CreateAsync(role);
                if (!identityResult.Succeeded)
                {
                    var errors = identityResult.Errors.Select(e => e.Description);
                    await tx.RollbackAsync();
                    throw new InvalidRoleDataException(string.Join(", ", errors));
                }

                var toAdd = validIds;
                if (toAdd.Count > 0)
                {
                    var rolePermissions = toAdd.Select(pid => new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = pid
                    });

                    _roleRepository.AddRolePermissions(rolePermissions);
                }

                await _unitOfWork.SaveChangesAsync();

                await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                    _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, role.Id);

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            var roleDetails = _mapper.Map<RoleDetailsDto>(role);
            var createdPermissions = await _roleRepository.GetRolePermissionsForRolesAsync(new[] { role.Id });
            roleDetails.Permissions = _mapper.Map<List<PermissionInfoDto>>(
                createdPermissions
                    .Where(rp => rp.Permission != null)
                    .Select(rp => rp.Permission!)
                    .ToList());

            roleDetails.PermissionsCount = roleDetails.Permissions.Count;

            return roleDetails;
        }

        #endregion


        #region             جلب عدد الادوار الفعالة 
        public async Task<string> GetActiveRolesCount()
        {
            var count = await _roleManager.Roles
                .Where(r => r.IsActive)
                .CountAsync();

            return count.ToString();
        }
        #endregion


        #region     جلب كل الصلاحيات الفعالة  
        public async Task<List<DropdownRoleDto>> GetDropdownRolesAsync()
        {
            var roles = await _roleRepository.QueryRoles()
                .Where(r => r.IsActive)
                .OrderBy(r => r.Name)
                .ToListAsync();

            return _mapper.Map<List<DropdownRoleDto>>(roles);
        }

        public async Task<RoleStatusUpdateDto> ToggleRoleStatusAsync(Guid roleId)
        {
            _permissionGate.EnsureAll(SystemPermissions.RoleManagement.ChangeStatus);

            var role = await _roleRepository.GetByIdAsync(roleId.ToString())
                ?? throw new RoleNotFoundException("الدور غير موجود.");

            role.IsActive = !role.IsActive;
            role.UpdatedAt = DateTime.Now;
            role.UpdatedBy = _currentUserService.UserId;

            _roleRepository.Update(role);
            await _unitOfWork.SaveChangesAsync();

            await SecurityStampHelper.InvalidateUsersWithRoleAsync(
                _userRepository, _refreshTokenRepository, _unitOfWork, _userManager, role.Id);

            return _mapper.Map<RoleStatusUpdateDto>(role);
        }

        #endregion

    }
}
