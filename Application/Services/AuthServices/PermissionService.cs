using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;

namespace Application.Services.AuthServices
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

        #region الانشاء 
        public async Task<PermissionDto> CreatePermissionAsync(CreatePermissionDto dto)
        {
            if (await _permissionRepository.ExistsByNameAsync(dto.Name))
                throw new InvalidRoleDataException("الصلاحية موجودة مسبقاً.");

            var permission = new Permission { Name = dto.Name, Description = dto.Description };
            _permissionRepository.Add(permission);
            await _unitOfWork.SaveChangesAsync();

            return new PermissionDto(permission.Id, permission.Name, permission.Description, permission.IsActive);
        }
        #endregion

        #region الحذف 
        public async Task DeletePermissionAsync(string id)
        {
            if (!Guid.TryParse(id, out var permissionId))
                throw new RoleNotFoundException("الصلاحية غير موجودة.");

            var permission = await _permissionRepository.FindByIdAsync(permissionId)
                ?? throw new RoleNotFoundException("الصلاحية غير موجودة.");

            _permissionRepository.Remove(permission);
            await _unitOfWork.SaveChangesAsync();
        }
        #endregion

        #region جلب حسب المعرف
        public async Task<PermissionDto> GetByIdAsync(string id)
        {
            if (!Guid.TryParse(id, out var permissionId))
                throw new RoleNotFoundException("الصلاحية غير موجودة.");

            var permission = await _permissionRepository.FindByIdAsync(permissionId)
                ?? throw new RoleNotFoundException("الصلاحية غير موجودة.");

            return new PermissionDto(permission.Id, permission.Name, permission.Description ?? string.Empty, permission.IsActive);
        }
        #endregion

        #region التعديل
        public async Task UpdatePermissionAsync(Guid id, UpdatePermissionDto dto)
        {
            var permission = await _permissionRepository.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("الصلاحية غير موجودة.");

            permission.Name = dto.Name;
            permission.Description = dto.Description;
            await _unitOfWork.SaveChangesAsync();
        }
        #endregion

        #region جلب الكل
        public async Task<List<PermissionDto>> GetPermissionsAsync()
        {
            var permissions = await _permissionRepository.GetAllOrderedAsync();
            return permissions
                .Select(p => new PermissionDto(p.Id, p.Name ?? string.Empty, p.Description ?? string.Empty, p.IsActive))
                .ToList();
        }
        #endregion

        #region جلب الصلاحيات مع الادوار التي ترتبط بها
        public async Task<List<PermissionWithRolesResultDto>> GetPermissionsWithRolesAsync(string? name = null, bool? isActive = null)
        {
            var nameFilter = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            var permissions = await _permissionRepository.GetWithRolesAsync(nameFilter, isActive);
            permissions = permissions.OrderBy(p => p.Name).ThenBy(p => p.Id).ToList();

            return permissions.Select(p => new PermissionWithRolesResultDto
            {
                Roles = p.RolePermissions.Select(rp => new RoleDto
                {
                    Id = rp.Role.Id,
                    Name = rp.Role.Name ?? string.Empty,
                    Description = rp.Role.Description,
                    IsActive = rp.Role.IsActive
                }).ToList(),

                Permissions = new List<PermissionWithRolesDto>
                {
                    new PermissionWithRolesDto
                    {
                        Id = p.Id,
                        Name = p.Name ?? string.Empty,
                        Description = p.Description,
                        IsActive = p.IsActive,
                        AssignedRoleIds = p.RolePermissions.Select(rp => rp.RoleId).ToList(),
                        AssignedRolesCount = p.RolePermissions.Count
                    }
                }
            }).ToList();
        }
        #endregion

        #region تعين او الغاء تعين صلاحية لدور معين
        public async Task TogglePermissionForRoleAsync(string roleId, Guid permissionId)
        {
            var role = await _roleManager.FindByIdAsync(roleId)
                ?? throw new RoleNotFoundException("الدور غير موجود.");

            var permission = await _permissionRepository.FindByIdAsync(permissionId)
                ?? throw new RoleNotFoundException("الصلاحية غير موجودة.");

            var existing = await _permissionRepository.FindRolePermissionAsync(roleId, permissionId);

            if (existing != null)
            {
                _permissionRepository.RemoveRolePermission(existing);
                await _unitOfWork.SaveChangesAsync();
                return;
            }

            var rolePermission = new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            };

            _permissionRepository.AddRolePermission(rolePermission);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task TogglePermissionStatusAsync(Guid permissionId)
        {
            var permission = await _permissionRepository.FindByIdAsync(permissionId)
                ?? throw new RoleNotFoundException("الصلاحية غير موجودة.");

            permission.IsActive = !permission.IsActive;
            _permissionRepository.Update(permission);
            await _unitOfWork.SaveChangesAsync();
        }
        #endregion
    }
}
