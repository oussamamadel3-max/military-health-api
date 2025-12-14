using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos.AuthDtos;

namespace ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
public interface IRoleService
{
    Task<Result<RoleDto>> CreateRoleAsync(CreateRoleDto newRole);
    Task<Result<string>> UpdateRoleAsync(string id, UpdateRoleDto updatedRole);
    Task<Result<string>> DeleteRoleAsync(string id);
    Task<Result<RoleDto>> GetById(string id);
    Task<Result<PaginatedList<RoleDetailsDto>>> GetRolesWithDetailsAsync(int pageNumber, int pageSize, string? name = null, bool? isActive = null);
    Task<Result<string>> AssignUserToRole(string userId, List<string> roleIds);
    Task<Result<string>> AssignPermissionsToRole(AssignPermissionsDto assingDto);
    Task<Result<RoleDetailsDto>> CreateRoleWithPermissionsAsync(CreateRoleWithPermissionsDto dto);
    Task<Result<List<DropdownRoleDto>>> GetDropdownRolesAsync();
    Task<Result<string>> GetActiveRolesCount();
    Task<Result<RoleStatusUpdateDto>> ToggleRoleStatusAsync(Guid roleId);

}
