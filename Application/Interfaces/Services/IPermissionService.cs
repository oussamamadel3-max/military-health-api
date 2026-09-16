using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;
public interface IPermissionService
{
    Task<PermissionDto> CreatePermissionAsync(CreatePermissionDto dto);
    Task UpdatePermissionAsync(Guid id, UpdatePermissionDto dto);
    Task DeletePermissionAsync(string id);
    Task<PermissionDto> GetByIdAsync(string id);
    Task<List<PermissionDto>> GetPermissionsAsync();
    Task<List<PermissionModuleDto>> GetPermissionModulesAsync();
    Task<List<PermissionWithRolesResultDto>> GetPermissionsWithRolesAsync(string? name = null, bool? isActive = null);
    Task TogglePermissionForRoleAsync(string roleId, Guid permissionId);
    Task TogglePermissionStatusAsync(Guid permissionId);
}
