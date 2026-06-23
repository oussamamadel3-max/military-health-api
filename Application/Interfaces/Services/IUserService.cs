using Application.Common.Results;
using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface IUserService
    {
        Task<Result<CreateUserDto>> Create(CreateUserDto createdUser);
        Task<Result<string>> Update(string id, UpdateUserDto updatedUser);
        Task<Result<string>> Delete(string userId);
        Task<Result<PaginatedList<UserListItemDto>>> GetUsersListAsync(int pageNumber, int pageSize, string? q = null, bool? isActive = null);
        Task<Result<UserListItemDto>> GetUserAsync(string id);
        Task<Result<string>> ChangeOwnPasswordAsync(ChangePasswordRequest request);
        Task<Result<string>> ResetUserPasswordAsync(AdminChangePasswordRequest request);
        Task<Result<string>> ToggleRolesForUserAsync(AssignRolesDto dto);
        Task<Result<string>> TogglePermissionsForUserAsync(AssignPermissionToUserDto dto);
        Task<Result<string>> RemoveFieldPermissionsFromUserAsync(RemoveFieldPermissionsDto dto);
        Task<Result<UserListItemDto>> UpdateUserAsync(string id, UpdateUserPatchRequest dto);
        Task<Result<UpdateUserStatusRequest>> UpdateUserStatusAsync(string id, UpdateUserStatusRequest request);
        Task<Result<string>> UpdateUserWithRolesAsync(string userId, UpdateUserWithRolesDto updatedUser);
        Task<Result<List<DropdownUserDto>>> GetDropDownUsersAsync();

    }
}

