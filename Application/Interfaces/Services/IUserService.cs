using Application.Common.Results;
using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services
{
    public interface IUserService
    {
        Task<CreatedUserResponseDto> Create(CreateUserDto createdUser);
        Task Update(string id, UpdateUserDto updatedUser);
        Task Delete(string userId);
        Task<PaginatedList<UserListItemDto>> GetUsersListAsync(
            int pageNumber,
            int pageSize,
            string? q = null,
            bool? isActive = null,
            string? roleId = null,
            Guid? centerId = null);
        Task<UserListItemDto> GetUserAsync(string id);
        Task ChangeOwnPasswordAsync(ChangePasswordRequest request);
        Task ResetUserPasswordAsync(AdminChangePasswordRequest request);
        Task ToggleRolesForUserAsync(AssignRolesDto dto);
        Task TogglePermissionsForUserAsync(AssignPermissionToUserDto dto);
        Task<DirectPermissionSyncResultDto> SyncDirectPermissionsAsync(SyncDirectPermissionsDto dto);
        /// <summary>
        /// قائمة الصلاحيات القابلة للإسناد المباشر ضمن نطاق المستخدم المستهدف (FR-018).
        /// تتطلب UserManagement.AssignPermissions ولا تتطلب PermissionManagement.Read.
        /// </summary>
        Task<AssignablePermissionsDto> GetAssignablePermissionsAsync(
            string userId,
            Guid? centerId = null,
            CancellationToken cancellationToken = default);
        Task RemoveFieldPermissionsFromUserAsync(RemoveFieldPermissionsDto dto);
        Task<UserListItemDto> UpdateUserAsync(string id, UpdateUserPatchRequest dto);
        Task<UpdateUserStatusRequest> UpdateUserStatusAsync(string id, UpdateUserStatusRequest request);
        Task UpdateUserWithRolesAsync(string userId, UpdateUserWithRolesDto updatedUser);
        Task<List<DropdownUserDto>> GetDropDownUsersAsync();
        Task UpdateMyProfileAsync(UpdateMyProfileRequest request);
    }
}
