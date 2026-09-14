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
        private readonly IUnitOfWork _unitOfWork;
        private readonly RoleManager<Role> _roleManager;
        private readonly IMapper _mapper;

        public PermissionService(
            IPermissionRepository permissionRepository,
            IUnitOfWork unitOfWork,
            RoleManager<Role> roleManager,
            IMapper mapper)
        {
            _permissionRepository = permissionRepository;
            _unitOfWork = unitOfWork;
            _roleManager = roleManager;
            _mapper = mapper;
        }

        #region الانشاء 
        public async Task<PermissionDto> CreatePermissionAsync(CreatePermissionDto dto)
        {
            if (await _permissionRepository.ExistsByNameAsync(dto.Name))
                throw new InvalidRoleDataException("الصلاحية موجودة مسبقاً.");

            var permission = _mapper.Map<Permission>(dto);
            _permissionRepository.Add(permission);
            await _unitOfWork.SaveChangesAsync();

            return _mapper.Map<PermissionDto>(permission);
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

            return _mapper.Map<PermissionDto>(permission);
        }
        #endregion

        #region التعديل
        public async Task UpdatePermissionAsync(Guid id, UpdatePermissionDto dto)
        {
            var permission = await _permissionRepository.FindByIdAsync(id)
                ?? throw new RoleNotFoundException("الصلاحية غير موجودة.");

            _mapper.Map(dto, permission);
            await _unitOfWork.SaveChangesAsync();
        }
        #endregion

        #region جلب الكل
        public async Task<List<PermissionDto>> GetPermissionsAsync()
        {
            var permissions = await _permissionRepository.GetAllOrderedAsync();
            return _mapper.Map<List<PermissionDto>>(permissions);
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
