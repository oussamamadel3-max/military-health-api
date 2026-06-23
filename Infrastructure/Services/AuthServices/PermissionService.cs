using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Dtos.AuthDtos;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Services.AuthServices
{
    public class PermissionService : IPermissionService
    {
        private readonly IPermissionRepository _permissionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly RoleManager<Role> _roleManager;

        public PermissionService(
            IPermissionRepository permissionRepository,
            IUnitOfWork unitOfWork,
            RoleManager<Role> roleManager)
        {
            _permissionRepository = permissionRepository;
            _unitOfWork = unitOfWork;
            _roleManager = roleManager;
        }


        #region         الانشاء 
        public async Task<Result<PermissionDto>> CreatePermissionAsync(CreatePermissionDto dto)
        {
            if (await _permissionRepository.ExistsByNameAsync(dto.Name))
                return Result<PermissionDto>.Failure(new List<string> { "الصلاحية موجودة مسبقاَ" }, null);

            var permission = new Permission { Name = dto.Name, Description = dto.Description };

            _permissionRepository.Add(permission);

            await _unitOfWork.SaveChangesAsync();

            return Result<PermissionDto>.Success(new PermissionDto(permission.Id, permission.Name, permission.Description, permission.IsActive));
        }
        #endregion


        #region         الحذف 
        public async Task<Result<string>> DeletePermissionAsync(string id)
        {
            if (!Guid.TryParse(id, out var permissionId))
                return Result<string>.Failure(new List<string> { "الصلاحية غير موجودة" }, null);

            var permission = await _permissionRepository.FindByIdAsync(permissionId);
            if (permission == null)
                return Result<string>.Failure(new List<string> { "الصلاحية غير موجودة" }, null);

            _permissionRepository.Remove(permission);
            await _unitOfWork.SaveChangesAsync();

            return Result<string>.Success("تم الحذف بنجاح");
        }
        #endregion


        #region         جلب حسب المعرف
        public async Task<Result<PermissionDto>> GetByIdAsync(string id)
        {
            var errors = new List<string>();

            var permission = await _permissionRepository.FindByIdAsync(Guid.Parse(id));

            if (permission == null)
            {
                errors.Add("الصلاحية غير موجودة");
                return Result<PermissionDto>.Failure(errors, null);
            }

            var dto = new PermissionDto(permission.Id, permission.Name, permission.Description ?? string.Empty, permission.IsActive);


            return Result<PermissionDto>.Success(dto);
        }
        #endregion


        #region         التعديل
        public async Task<Result<string>> UpdatePermissionAsync(Guid id, UpdatePermissionDto dto)
        {
            var permission = await _permissionRepository.FindByIdAsync(id);
            if (permission == null)
                return Result<string>.Failure(new List<string> { "الصلاحية غير موجودة" }, null);

            permission.Name = dto.Name;
            permission.Description = dto.Description;
            await _unitOfWork.SaveChangesAsync();

            return Result<string>.Success("تم التحديث بنجاح");
        }
        #endregion


        #region      جلب الكل
        public async Task<Result<List<PermissionDto>>> GetPermissionsAsync()
        {
            var permissions = await _permissionRepository.GetAllOrderedAsync();

            var items = permissions
                .Select(p => new PermissionDto(p.Id, p.Name ?? string.Empty, p.Description ?? string.Empty, p.IsActive))
                .ToList();

            return Result<List<PermissionDto>>.Success(items);
        }

        #endregion


        #region         جلب الصلاحيات مع الادوار التي ترتبط بها
        public async Task<Result<List<PermissionWithRolesResultDto>>> GetPermissionsWithRolesAsync(string? name = null, bool? isActive = null)
        {
            try
            {
                var nameFilter = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
                var permissions = await _permissionRepository.GetWithRolesAsync(nameFilter, isActive);
                permissions = permissions.OrderBy(p => p.Name).ThenBy(p => p.Id).ToList();

                var permissionDtos = permissions.Select(p => new PermissionWithRolesResultDto
                {
                    Roles = p.RolePermissions.Select(rp => new RoleDto
                    {
                        Id = rp.Role.Id,
                        Name = rp.Role.Name,
                        Description = rp.Role.Description,
                        IsActive = rp.Role.IsActive
                    }).ToList(),

                    Permissions = new List<PermissionWithRolesDto>
            {
                new PermissionWithRolesDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    IsActive = p.IsActive,
                    AssignedRoleIds = p.RolePermissions.Select(rp => rp.RoleId).ToList(),
                    AssignedRolesCount = p.RolePermissions.Count
                }
            }
                }).ToList();

                return Result<List<PermissionWithRolesResultDto>>.Success(permissionDtos);
            }
            catch (Exception ex)
            {
                return Result<List<PermissionWithRolesResultDto>>
                    .Failure(new List<string> { ex.Message }, null);
            }
        }


        #endregion


        #region         تعين او الغاء تعين صلاحية لدور معين
        public async Task<Result<string>> TogglePermissionForRoleAsync(string roleId, Guid permissionId)
        {
            var errors = new List<string>();

            var role = await _roleManager.FindByIdAsync(roleId);
            if (role == null)
            {
                errors.Add("الدور غير موجود");
                return Result<string>.Failure(errors, null);
            }

            var permission = await _permissionRepository.FindByIdAsync(permissionId);
            if (permission == null)
            {
                errors.Add("الصلاحية غير موجودة");
                return Result<string>.Failure(errors, null);
            }

            var existing = await _permissionRepository.FindRolePermissionAsync(roleId, permissionId);

            if (existing != null)
            {
                _permissionRepository.RemoveRolePermission(existing);
                await _unitOfWork.SaveChangesAsync();
                return Result<string>.Success("تم إلغاء اسناد الصلاحية للدور بنجاح");
            }

            var rolePermission = new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            };

            _permissionRepository.AddRolePermission(rolePermission);
            await _unitOfWork.SaveChangesAsync();
            return Result<string>.Success("تم اسناد الصلاحية للدور بنجاح");
        }

        public async Task<Result<string>> TogglePermissionStatusAsync(Guid permissionId)
        {
            var errors = new List<string>();

            var permission = await _permissionRepository.FindByIdAsync(permissionId);
            if (permission == null)
            {
                errors.Add("الصلاحية غير موجودة");
                return Result<string>.Failure(errors, null);
            }

            permission.IsActive = !permission.IsActive;

            _permissionRepository.Update(permission);
            await _unitOfWork.SaveChangesAsync();

            var status = permission.IsActive ? "مفعلة" : "معطلة";
            return Result<string>.Success($"تم تغيير حالة الصلاحية إلى: {status}");
        }

        #endregion


    }
}
