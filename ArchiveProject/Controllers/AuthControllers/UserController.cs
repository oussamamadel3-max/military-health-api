using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Application.Common.Interfaces.UserContextInterfaces;
using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.Dtos.AuthDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.API.Controllers.AuthControllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IUserContextService _userContextService;

        public UserController(IUserService userService, IUserContextService userContextService)
        {
            _userService = userService;
            _userContextService = userContextService;
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Create")]
        public async Task<Result<CreateUserDto>> Create(CreateUserDto userDto)
        {
            var createdUser = await _userService.Create(userDto);

            if (createdUser.Value == null || !createdUser.Succeeded)
            {
                return Result<CreateUserDto>.Failure(createdUser.Errors, null);
            }

            return Result<CreateUserDto>.Success(createdUser.Value);
        }

        [HttpPut("[action]/{id}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<string>> Update(string id, UpdateUserDto updatedUser)
        {
            var result = await _userService.Update(id, updatedUser);

            if (result.Value == null || !result.Succeeded)
            {
                return Result<string>.Failure(result.Errors, null);
            }

            return Result<string>.Success(result.Value);
        }

        [HttpDelete("[action]/{userId}")]
        [AuthorizePermission("UserManagement.Delete")]
        public async Task<Result<string>> Delete(string userId)
        {
            var result = await _userService.Delete(userId);

            if (result.Value == null || !result.Succeeded)
            {
                return Result<string>.Failure(result.Errors, null);
            }

            return Result<string>.Success(result.Value);
        }

        [HttpGet("[action]")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<PaginatedList<UserListItemDto>>> GetUsersList(
            [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
            [FromQuery][Range(1, 500)] int pageSize = 10,
            [FromQuery] string? q = null,
            [FromQuery] bool? isActive = null)
        {
            var result = await _userService.GetUsersListAsync(pageNumber, pageSize, q, isActive);

            if (!result.Succeeded || result.Value == null)
                return Result<PaginatedList<UserListItemDto>>.Failure(result.Errors, null);

            return Result<PaginatedList<UserListItemDto>>.Success(result.Value);
        }


        [HttpGet("[action]/{id}")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<UserListItemDto>> GetUser(string id)
        {
            var user = await _userService.GetUserAsync(id);

            if (user.Value == null || !user.Succeeded)
            {
                return Result<UserListItemDto>.Failure(user.Errors, null);
            }
            return Result<UserListItemDto>.Success(user.Value);
        }

        [HttpPatch("[action]/{id}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<UserListItemDto>> PatchUser(string id, [FromBody] UpdateUserPatchRequest request)
        {
            if (!ModelState.IsValid)
                return Result<UserListItemDto>.Failure(new[] { "Invalid model state" }, null);

            var result = await _userService.UpdateUserAsync(id, request);

            return result.Succeeded && result.Value != null
                ? Result<UserListItemDto>.Success(result.Value)
                : Result<UserListItemDto>.Failure(result.Errors, null);
        }


        [HttpPatch("[action]/{id}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<UpdateUserStatusRequest>> UpdateUserStatus(string id, [FromBody] UpdateUserStatusRequest request)
        {
            if (!ModelState.IsValid)
                return Result<UpdateUserStatusRequest>.Failure(new[] { "Invalid model state" }, null);

            var result = await _userService.UpdateUserStatusAsync(id, request);

            return result.Succeeded
                ? Result<UpdateUserStatusRequest>.Success(result.Value!)
                : Result<UpdateUserStatusRequest>.Failure(result.Errors, null);
        }


        [HttpPost("[action]")]
        public async Task<Result<string>> ChangeMyPassword([FromBody] ChangePasswordRequest dto)
        {

            var result = await _userService.ChangeOwnPasswordAsync(dto);

            if (result.Value == null || !result.Succeeded)
            {
                return Result<string>.Failure(result.Errors, null);
            }

            return Result<string>.Success(result.Value);
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<string>> ChangePasswordUser(AdminChangePasswordRequest dto)
        {
            var result = await _userService.ResetUserPasswordAsync(dto);

            if (result.Value == null || !result.Succeeded)
            {
                return Result<string>.Failure(result.Errors, null);
            }

            return Result<string>.Success(result.Value);
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<string>> ToggleRolesForUserAsync([FromBody] AssignRolesDto dto)
        {
            var result = await _userService.ToggleRolesForUserAsync(dto);

            if (result.Value == null || !result.Succeeded)
            {
                return Result<string>.Failure(result.Errors, null);
            }
            return Result<string>.Success(result.Value);
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<string>> TogglePermissionsForUserAsync([FromBody] AssignPermissionToUserDto dto)
        {
            var result = await _userService.TogglePermissionsForUserAsync(dto);

            if (result.Value == null || !result.Succeeded)
            {
                return Result<string>.Failure(result.Errors, null);
            }

            return Result<string>.Success(result.Value);
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<string>> RemoveFieldPermissionsFromUser([FromBody] RemoveFieldPermissionsDto dto)
        {
            var result = await _userService.RemoveFieldPermissionsFromUserAsync(dto);

            if (result.Value == null || !result.Succeeded)
            {
                return Result<string>.Failure(result.Errors, null);
            }

            return Result<string>.Success(result.Value);
        }

        [HttpPut("[action]/{id}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<string>> UpdateUserWithRoles([FromRoute] string id, [FromBody] UpdateUserWithRolesDto dto)
        {
            var result = await _userService.UpdateUserWithRolesAsync(id, dto);

            if (result.Value == null || !result.Succeeded)
            {
                return Result<string>.Failure(result.Errors, null);
            }

            return Result<string>.Success(result.Value);
        }


        [HttpGet("[action]")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<List<DropdownUserDto>>> GetDropDownUsers()
        {
            var result = await _userService.GetDropDownUsersAsync();

            if (result.Value == null || !result.Succeeded)
            {
                return Result<List<DropdownUserDto>>.Failure(result.Errors, null);
            }
            return Result<List<DropdownUserDto>>.Success(result.Value);
        }
    }
}

