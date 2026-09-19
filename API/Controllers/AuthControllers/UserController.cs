using API.Infrastructure;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.AuthControllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Create")]
        public async Task<IResult> Create([FromBody] CreateUserDto userDto)
        {
            var createdUser = await _userService.Create(userDto);
            return ApiResponse.Success("تم إنشاء المستخدم بنجاح.", createdUser);
        }

        [HttpPut("[action]/{id}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<IResult> Update(string id, [FromBody] UpdateUserDto updatedUser)
        {
            await _userService.Update(id, updatedUser);
            return ApiResponse.Success("تم تحديث المستخدم بنجاح.");
        }

        [HttpDelete("[action]/{userId}")]
        [AuthorizePermission("UserManagement.Delete")]
        public async Task<IResult> Delete(string userId)
        {
            await _userService.Delete(userId);
            return ApiResponse.Success("تم حذف المستخدم بنجاح.");
        }

        [HttpGet("[action]")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<IResult> GetUsersList(
            [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
            [FromQuery][Range(1, 500)] int pageSize = 10,
            [FromQuery] string? q = null,
            [FromQuery] bool? isActive = null,
            [FromQuery] string? roleId = null)
        {
            var usersList = await _userService.GetUsersListAsync(pageNumber, pageSize, q, isActive, roleId);
            return ApiResponse.Success("تم جلب قائمة المستخدمين بنجاح.", usersList);
        }

        [HttpGet("[action]/{id}")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<IResult> GetUser(string id)
        {
            var user = await _userService.GetUserAsync(id);
            return ApiResponse.Success("تم جلب بيانات المستخدم بنجاح.", user);
        }

        [HttpPatch("[action]/{id}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<IResult> PatchUser(string id, [FromBody] UpdateUserPatchRequest request)
        {
            var updatedUser = await _userService.UpdateUserAsync(id, request);
            return ApiResponse.Success("تم التعديل الجزئي بنجاح.", updatedUser);
        }

        [HttpPatch("[action]/{id}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<IResult> UpdateUserStatus(string id, [FromBody] UpdateUserStatusRequest request)
        {
            var updatedStatus = await _userService.UpdateUserStatusAsync(id, request);
            return ApiResponse.Success("تم تعديل حالة المستخدم بنجاح.", updatedStatus);
        }

        [HttpPost("[action]")]
        public async Task<IResult> ChangeMyPassword([FromBody] ChangePasswordRequest dto)
        {
            await _userService.ChangeOwnPasswordAsync(dto);
            return ApiResponse.Success("تم تغيير كلمة المرور بنجاح.");
        }

        [HttpPut("[action]")]
        public async Task<IResult> UpdateMyProfile([FromBody] UpdateMyProfileRequest dto)
        {
            await _userService.UpdateMyProfileAsync(dto);
            return ApiResponse.Success("تم تحديث الملف الشخصي بنجاح.");
        }

        [HttpPost("[action]")]
        [Consumes("multipart/form-data")]
        public async Task<IResult> UploadMyProfilePhoto(IFormFile file)
        {
            var photoUrl = await _userService.UploadMyProfilePhotoAsync(file);
            return ApiResponse.Success("تم رفع الصورة الشخصية بنجاح.", new { profilePhotoUrl = photoUrl });
        }

        [HttpGet("[action]")]
        public async Task<IActionResult> MyProfilePhoto()
        {
            var (bytes, contentType, _) = await _userService.GetMyProfilePhotoAsync();
            return File(bytes, contentType);
        }

        [HttpDelete("[action]")]
        public async Task<IResult> RemoveMyProfilePhoto()
        {
            await _userService.RemoveMyProfilePhotoAsync();
            return ApiResponse.Success("تم حذف الصورة الشخصية بنجاح.");
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<IResult> ChangePasswordUser([FromBody] AdminChangePasswordRequest dto)
        {
            await _userService.ResetUserPasswordAsync(dto);
            return ApiResponse.Success("تم إعادة تعيين كلمة المرور بنجاح.");
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<IResult> ToggleRolesForUserAsync([FromBody] AssignRolesDto dto)
        {
            await _userService.ToggleRolesForUserAsync(dto);
            return ApiResponse.Success("تم تعديل أدوار المستخدم بنجاح.");
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<IResult> TogglePermissionsForUserAsync([FromBody] AssignPermissionToUserDto dto)
        {
            await _userService.TogglePermissionsForUserAsync(dto);
            return ApiResponse.Success("تم تعديل صلاحيات المستخدم بنجاح.");
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<IResult> RemoveFieldPermissionsFromUser([FromBody] RemoveFieldPermissionsDto dto)
        {
            await _userService.RemoveFieldPermissionsFromUserAsync(dto);
            return ApiResponse.Success("تم إزالة الصلاحيات الحقلية بنجاح.");
        }

        [HttpPut("[action]/{id}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<IResult> UpdateUserWithRoles([FromRoute] string id, [FromBody] UpdateUserWithRolesDto dto)
        {
            await _userService.UpdateUserWithRolesAsync(id, dto);
            return ApiResponse.Success("تم تحديث المستخدم والأدوار بنجاح.");
        }

        [HttpGet("[action]")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<IResult> GetDropDownUsers()
        {
            var dropDownUsers = await _userService.GetDropDownUsersAsync();
            return ApiResponse.Success("تم جلب قائمة المستخدمين بنجاح.", dropDownUsers);
        }
    }
}
