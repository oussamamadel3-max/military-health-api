using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

public interface IUserAssignmentService
{
    Task<UserAssignmentsDto> GetAssignmentsAsync(string userId, CancellationToken cancellationToken = default);
    Task<UserCenterAssignmentDto> AssignCenterAsync(AssignUserCenterDto dto, CancellationToken cancellationToken = default);
    Task RevokeCenterAsync(RevokeUserCenterDto dto, CancellationToken cancellationToken = default);
    Task<UserRoleAssignmentDto> AssignRoleAsync(AssignUserRoleDto dto, CancellationToken cancellationToken = default);
    Task RevokeRoleAsync(RevokeUserRoleDto dto, CancellationToken cancellationToken = default);
    Task<List<DropdownRoleDto>> GetAssignableRolesAsync(CancellationToken cancellationToken = default);
}
