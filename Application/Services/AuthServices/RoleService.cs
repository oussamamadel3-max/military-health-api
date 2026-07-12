using AutoMapper;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Common.Utilities;
using Application.Common.Results;
using Application.Dtos.AuthDtos;
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

        public RoleService(
            RoleManager<Role> roleManager,
            IUserRepository userRepository,
            UserManager<User> userManager,
            IMapper mapper,
            ICurrentUserService currentUserService,
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
        }

        #region         الانشاء
        public async Task<Result<RoleDto>> CreateRoleAsync(CreateRoleDto newRole)
        {
            var errorList = new List<string>();

            var role = _mapper.Map<Role>(newRole);

            role.CreatedAt = DateTime.Now;
            role.CreatedBy = _currentUserService.UserId;

            var identityResult = await _roleManager.CreateAsync(role);

            if (!identityResult.Succeeded)
            {
                errorList.AddRange(identityResult.Errors.Select(e => e.Description));
                return Result<RoleDto>.Failure(errorList, null);
            }

            var roleDto = _mapper.Map<RoleDto>(role);

            return Result<RoleDto>.Success(roleDto);
        }
        #endregion


        #region         التعديل
        public async Task<Result<string>> UpdateRoleAsync(string id, UpdateRoleDto updatedRole)
        {
            var errorList = new List<string>();

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                errorList.Add("الدور غير موجود.");
                return Result<string>.Failure(errorList, null);
            }

            _mapper.Map(updatedRole, role);

            role.UpdatedAt = DateTime.Now;
            role.UpdatedBy = _currentUserService.UserId;

            var identityResult = await _roleManager.UpdateAsync(role);
            if (!identityResult.Succeeded)
            {
                errorList.AddRange(identityResult.Errors.Select(e => e.Description));
                return Result<string>.Failure(errorList, null);
            }

            return Result<string>.Success("تم تحديث الدور بنجاح.");
        }
        #endregion


        #region         الحذف
        public async Task<Result<string>> DeleteRoleAsync(string id)
        {
            var errorList = new List<string>();

            var role = await _roleManager.FindByIdAsync(id);

            if (role == null)
            {
                errorList.Add("الدور غير موجود");
                return Result<string>.Failure(errorList, null);
            }

            await _roleManager.DeleteAsync(role);

            return Result<string>.Success("تم الحذف بنجاح");
        }
        #endregion


        #region      جلب حسب المعرف 
        public async Task<Result<RoleDto>> GetById(string id)
        {
            var errorList = new List<string>();

            var role = _roleManager.Roles
                .FirstOrDefault(r => r.Id == id);

            if (role == null)
            {
                await Task.Delay(1);
                errorList.Add("الدور غير موجود");
                return Result<RoleDto>.Failure(errorList, null);
            }

            var roleDto = new RoleDto();

            roleDto.Name = role.Name ?? string.Empty;

            roleDto.Id = role.Id;

            return Result<RoleDto>.Success(roleDto);
        }
        #endregion


        #region          جلب الصلاحيات مع تفاصيلها 
        public async Task<Result<PaginatedList<RoleDetailsDto>>> GetRolesWithDetailsAsync(int pageNumber, int pageSize, string? name = null, bool? isActive = null)
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

            rolesQuery = rolesQuery.Where(r => r.NormalizedName != "SUPERADMIN");

            rolesQuery = rolesQuery.OrderBy(r => r.Name);

            var totalRecords = await rolesQuery.CountAsync();

            var roles = await rolesQuery
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            if (roles.Count == 0)
            {
                var empty = new PaginatedList<RoleDetailsDto>(new List<RoleDetailsDto>(), totalRecords, pageNumber, pageSize);
                return Result<PaginatedList<RoleDetailsDto>>.Success(empty);
            }

            var roleIds = roles.Select(r => r.Id).ToList();

            var rolePermissions = await _roleRepository.GetRolePermissionsForRolesAsync(roleIds);
            var permissionsDict = rolePermissions
                .Where(rp => rp.Permission != null)
                .GroupBy(rp => rp.RoleId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(rp => new PermissionInfoDto
                    {
                        Id = rp.Permission!.Id,
                        Name = AuthDisplayNames.PermissionAr(rp.Permission.Name)
                    }).ToList());

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

            var result = new PaginatedList<RoleDetailsDto>(roleDtos, totalRecords, pageNumber, pageSize);
            return Result<PaginatedList<RoleDetailsDto>>.Success(result);
        }

        #endregion


        #region         اسناد مستخدم الى دور 
        public async Task<Result<string>> AssignUserToRole(string userId, List<string> roleIds)
        {
            var errorList = new List<string>();

            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
            {
                errorList.Add("المستخدم غير موجود");
                return Result<string>.Failure(errorList, null);
            }

            var currentRoles = await _userManager.GetRolesAsync(user);

            var activeRoles = await _roleManager.Roles
                .Where(r => roleIds.Contains(r.Id) && r.IsActive)
                .Select(r => new { r.Id, r.Name })
                .ToListAsync();

            var activeRoleIds = activeRoles.Select(r => r.Id).ToList();
            var inactiveRoleIds = roleIds.Except(activeRoleIds).ToList();

            if (inactiveRoleIds.Any())
            {
                errorList.Add($"الأدوار التالية غير نشطة: {string.Join(", ", inactiveRoleIds)}");
                return Result<string>.Failure(errorList, null);
            }

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

            return Result<string>.Success("Save role request");
        }
        #endregion


        #region         اسناد صلاحية الى دور 
        public async Task<Result<string>> SyncPermissionsForRoleAsync(AssignPermissionsDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.RoleId))
                return Result<string>.Failure(new[] { "RoleId مطلوب." }, null);

            var role = await _roleManager.FindByIdAsync(dto.RoleId);
            if (role == null)
                return Result<string>.Failure(new[] { "الدور غير موجود." }, null);

            role.UpdatedAt = DateTime.Now;
            role.UpdatedBy = _currentUserService.UserId;

            var requestedIds = (dto.PermissionsIds ?? new List<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            var activeValidIds = await _permissionRepository.GetActiveIdsAsync(requestedIds);

            var missingOrInactive = requestedIds.Except(activeValidIds).ToList();
            if (missingOrInactive.Any())
                return Result<string>.Failure(
                    new[] { $"صلاحيات غير موجودة أو غير نشطة: {string.Join(", ", missingOrInactive)}" }, null);

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

                return Result<string>.Success($"تم تحديث الصلاحيات بنجاح. (إضافة: {toAdd.Count} | حذف: {toRemove.Count})");
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        #endregion


        #region          انشاء دور مع الصلاحيات
        public async Task<Result<RoleDetailsDto>> CreateRoleWithPermissionsAsync(CreateRoleWithPermissionsDto dto)
        {
            var errors = new List<string>();

            var roleName = (dto.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(roleName))
            {
                errors.Add("اسم الدور مطلوب.");
                return Result<RoleDetailsDto>.Failure(errors, null);
            }

            var existingRole = await _roleManager.FindByNameAsync(roleName);
            if (existingRole != null)
            {
                errors.Add("الدور موجود مسبقاً، لا يمكن إضافته مرة أخرى.");
                return Result<RoleDetailsDto>.Failure(errors, null);
            }

            var inputIds = dto.PermissionIds ?? Enumerable.Empty<Guid>();
            var requestedDistinct = inputIds.Distinct().ToList();
            var inputDuplicatesSkipped = inputIds.Count() - requestedDistinct.Count;

            var validIds = await _permissionRepository.GetActiveIdsAsync(requestedDistinct);

            var inactiveIds = requestedDistinct.Except(validIds).ToList();
            if (inactiveIds.Any())
            {
                errors.Add($"الصلاحيات التالية غير نشطة: {string.Join(", ", inactiveIds)}");
                return Result<RoleDetailsDto>.Failure(errors, null);
            }

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
                    errors.AddRange(identityResult.Errors.Select(e => e.Description));
                    await tx.RollbackAsync();
                    return Result<RoleDetailsDto>.Failure(errors, null);
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
            catch (DbUpdateException dbEx)
            {
                await tx.RollbackAsync();
                errors.Add("فشل في حفظ البيانات. يرجى المحاولة لاحقاً.");
                errors.Add(dbEx.GetBaseException().Message);
                return Result<RoleDetailsDto>.Failure(errors, null);
            }

            var roleDetails = _mapper.Map<RoleDetailsDto>(role);
            var createdPermissions = await _roleRepository.GetRolePermissionsForRolesAsync(new[] { role.Id });
            roleDetails.Permissions = createdPermissions
                .Where(rp => rp.Permission != null)
                .Select(rp => new PermissionInfoDto
                {
                    Id = rp.Permission!.Id,
                    Name = rp.Permission.Name ?? string.Empty
                })
                .ToList();

            roleDetails.PermissionsCount = roleDetails.Permissions.Count;

            var addedCount = roleDetails.PermissionsCount;
            var skippedCount = invalidSkipped + inputDuplicatesSkipped;
            _ = $"تم إنشاء الدور بنجاح. تمت إضافة {addedCount} صلاحية. " +
                         $"تم تجاوز {skippedCount} (غير موجودة أو مكررة في الإدخال).";

            return Result<RoleDetailsDto>.Success(roleDetails);
        }

        #endregion


        #region             جلب عدد الادوار الفعالة 
        public async Task<Result<string>> GetActiveRolesCount()
        {
            var count = await _roleManager.Roles
                .Where(r => r.IsActive)
                .CountAsync();

            return Result<string>.Success(count.ToString());
        }
        #endregion


        #region     جلب كل الصلاحيات الفعالة  
        public async Task<Result<List<DropdownRoleDto>>> GetDropdownRolesAsync()
        {
            var roles = await _roleRepository.QueryRoles()
                .Where(r => r.IsActive)
                .OrderBy(r => r.Name)
                .Select(r => new DropdownRoleDto
                {
                    roleId = r.Id,
                    name = AuthDisplayNames.RoleAr(r.Name ?? string.Empty)
                })
                .ToListAsync();

            return Result<List<DropdownRoleDto>>.Success(roles);
        }

        public async Task<Result<RoleStatusUpdateDto>> ToggleRoleStatusAsync(Guid roleId)
        {
            var role = await _roleRepository.GetByIdAsync(roleId.ToString());
            if (role == null)
                return Result<RoleStatusUpdateDto>.Failure(new List<string> { $"الدور غير موجود." }, null);

            role.IsActive = !role.IsActive;
            role.UpdatedAt = DateTime.Now;
            role.UpdatedBy = _currentUserService.UserId;

            _roleRepository.Update(role);
            await _unitOfWork.SaveChangesAsync();

            var dto = _mapper.Map<RoleStatusUpdateDto>(role);

            return Result<RoleStatusUpdateDto>.Success(dto);
        }

        #endregion


    }
}
