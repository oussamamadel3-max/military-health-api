using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;
public interface IPermissionService
{
    Task<Result<PermissionDto>> CreatePermissionAsync(CreatePermissionDto dto);
    Task<Result<string>> UpdatePermissionAsync(Guid id, UpdatePermissionDto dto);
    Task<Result<string>> DeletePermissionAsync(string id);
    Task<Result<PermissionDto>> GetByIdAsync(string id);
    Task<Result<List<PermissionDto>>> GetPermissionsAsync();
    Task<Result<List<PermissionWithRolesResultDto>>> GetPermissionsWithRolesAsync(string? name = null, bool? isActive = null);
    Task<Result<string>> TogglePermissionForRoleAsync(string roleId, Guid permissionId);

    // IsActive or not
    Task<Result<string>> TogglePermissionStatusAsync(Guid permissionId);

}

