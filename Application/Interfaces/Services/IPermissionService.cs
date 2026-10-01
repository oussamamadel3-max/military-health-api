using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;
public interface IPermissionService
{
    Task<PermissionDto> GetByIdAsync(string id);
    Task<List<PermissionDto>> GetPermissionsAsync();
    Task<List<PermissionModuleDto>> GetPermissionModulesAsync();
    Task<List<PermissionWithRolesResultDto>> GetPermissionsWithRolesAsync(string? name = null, bool? isActive = null);
    Task TogglePermissionForRoleAsync(string roleId, Guid permissionId);
    Task TogglePermissionStatusAsync(Guid permissionId);
}
