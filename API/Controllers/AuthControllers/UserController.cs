using API.Infrastructure;
using Application.Common.Permissions;
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
        private readonly IUserAssignmentService _userAssignments;
        private readonly IUserClinicAssignmentService _userClinicAssignments;
        private readonly IUserEffectiveAccessService _effectiveAccess;

        public UserController(
            IUserService userService,
            IUserAssignmentService userAssignments,
            IUserClinicAssignmentService userClinicAssignments,
            IUserEffectiveAccessService effectiveAccess)
        {
            _userService = userService;
            _userAssignments = userAssignments;
            _userClinicAssignments = userClinicAssignments;
            _effectiveAccess = effectiveAccess;
        }

        [HttpPost("[action]")]
        [AuthorizePermission(
            SystemPermissions.UserManagement.Create,
            SystemPermissions.UserManagement.AssignRoles,
            MilitaryHealthPermissions.UserCenters.Manage)]
        public async Task<IResult> Create([FromBody] CreateUserDto userDto)
        {
            var createdUser = await _userService.Create(userDto);
            return ApiResponse.Success("تم إنشاء المستخدم بنجاح.", createdUser);
        }

        [HttpPut("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.UserManagement.Edit)]
        public async Task<IResult> Update(string id, [FromBody] UpdateUserDto updatedUser)
        {
            await _userService.Update(id, updatedUser);
            return ApiResponse.Success("تم تحديث المستخدم بنجاح.");
        }

        [HttpDelete("[action]/{userId}")]
        [AuthorizePermission(SystemPermissions.UserManagement.Delete)]
        public async Task<IResult> Delete(string userId)
        {
            await _userService.Delete(userId);
            return ApiResponse.Success("تم حذف المستخدم بنجاح.");
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.UserManagement.Read)]
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
        [AuthorizePermission(SystemPermissions.UserManagement.Read)]
        public async Task<IResult> GetUser(string id)
        {
            var user = await _userService.GetUserAsync(id);
            return ApiResponse.Success("تم جلب بيانات المستخدم بنجاح.", user);
        }

        [HttpPatch("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.UserManagement.Edit)]
        public async Task<IResult> PatchUser(string id, [FromBody] UpdateUserPatchRequest request)
        {
            var updatedUser = await _userService.UpdateUserAsync(id, request);
            return ApiResponse.Success("تم التعديل الجزئي بنجاح.", updatedUser);
        }

        [HttpPatch("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.UserManagement.ChangeStatus)]
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
        [AuthorizePermission(SystemPermissions.UserManagement.ResetPassword)]
        public async Task<IResult> ChangePasswordUser([FromBody] AdminChangePasswordRequest dto)
        {
            await _userService.ResetUserPasswordAsync(dto);
            return ApiResponse.Success("تم إعادة تعيين كلمة المرور بنجاح.");
        }

        [HttpPost("[action]")]
        [AuthorizePermission(SystemPermissions.UserManagement.AssignRoles)]
        public async Task<IResult> ToggleRolesForUserAsync([FromBody] AssignRolesDto dto)
        {
            await _userService.ToggleRolesForUserAsync(dto);
            return ApiResponse.Success("تم تعديل أدوار المستخدم بنجاح.");
        }

        [HttpPost("[action]")]
        [AuthorizePermission(SystemPermissions.UserManagement.AssignPermissions)]
        public async Task<IResult> TogglePermissionsForUserAsync([FromBody] AssignPermissionToUserDto dto)
        {
            await _userService.TogglePermissionsForUserAsync(dto);
            return ApiResponse.Success("تم تعديل صلاحيات المستخدم بنجاح.");
        }

        /// <summary>
        /// إزالة روابط صلاحيات الحقول عن مستخدم. يتطلب UserManagement.RemoveFieldPermissions.
        /// يُبطل جلسات المستخدم بعد الإزالة.
        /// </summary>
        [HttpPost("[action]")]
        [AuthorizePermission(SystemPermissions.UserManagement.RemoveFieldPermissions)]
        public async Task<IResult> RemoveFieldPermissionsFromUser([FromBody] RemoveFieldPermissionsDto dto)
        {
            await _userService.RemoveFieldPermissionsFromUserAsync(dto);
            return ApiResponse.Success("تم إزالة الصلاحيات الحقلية بنجاح.");
        }

        [HttpPut("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.UserManagement.Edit, SystemPermissions.UserManagement.AssignRoles)]
        public async Task<IResult> UpdateUserWithRoles([FromRoute] string id, [FromBody] UpdateUserWithRolesDto dto)
        {
            await _userService.UpdateUserWithRolesAsync(id, dto);
            return ApiResponse.Success("تم تحديث المستخدم والأدوار بنجاح.");
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.UserManagement.AssignRoles)]
        public async Task<IResult> AssignableRoles(CancellationToken cancellationToken)
        {
            var roles = await _userAssignments.GetAssignableRolesAsync(cancellationToken);
            return ApiResponse.Success("تم جلب الأدوار القابلة للإسناد.", roles);
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.UserManagement.Read)]
        public async Task<IResult> GetDropDownUsers()
        {
            var dropDownUsers = await _userService.GetDropDownUsersAsync();
            return ApiResponse.Success("تم جلب قائمة المستخدمين بنجاح.", dropDownUsers);
        }

        [HttpGet("[action]/{userId}")]
        [AuthorizePermission(SystemPermissions.UserManagement.Read)]
        public async Task<IResult> GetUserAssignments(string userId, CancellationToken cancellationToken)
        {
            var assignments = await _userAssignments.GetAssignmentsAsync(userId, cancellationToken);
            return ApiResponse.Success("تم جلب إسنادات المستخدم بنجاح.", assignments);
        }

        /// <summary>
        /// عرض الصلاحيات الفعلية (FR-021). يسمح لصاحب الحساب أو لمن لديه UserManagement.Read ضمن تفويضه.
        /// </summary>
        [HttpGet("[action]/{userId}")]
        public async Task<IResult> GetEffectivePermissions(
            string userId,
            [FromQuery] Guid? centerId = null,
            CancellationToken cancellationToken = default)
        {
            var result = await _effectiveAccess.GetEffectiveAccessAsync(userId, centerId, cancellationToken);
            return ApiResponse.Success("تم جلب الصلاحيات الفعلية بنجاح.", result);
        }

        [HttpPost("[action]")]
        [AuthorizePermission(MilitaryHealthPermissions.UserCenters.Manage)]
        public async Task<IResult> AssignUserCenter([FromBody] AssignUserCenterDto dto, CancellationToken cancellationToken)
        {
            var result = await _userAssignments.AssignCenterAsync(dto, cancellationToken);
            return ApiResponse.Success("تم إسناد المركز بنجاح.", result);
        }

        [HttpPost("[action]")]
        [AuthorizePermission(MilitaryHealthPermissions.UserCenters.Manage)]
        public async Task<IResult> RevokeUserCenter([FromBody] RevokeUserCenterDto dto, CancellationToken cancellationToken)
        {
            await _userAssignments.RevokeCenterAsync(dto, cancellationToken);
            return ApiResponse.Success("تم إلغاء إسناد المركز بنجاح.");
        }

        [HttpPost("[action]")]
        [AuthorizePermission(SystemPermissions.UserManagement.AssignRoles)]
        public async Task<IResult> AssignUserRole([FromBody] AssignUserRoleDto dto, CancellationToken cancellationToken)
        {
            var result = await _userAssignments.AssignRoleAsync(dto, cancellationToken);
            return ApiResponse.Success("تم إسناد الدور بنجاح.", result);
        }

        [HttpPost("[action]")]
        [AuthorizePermission(SystemPermissions.UserManagement.AssignRoles)]
        public async Task<IResult> RevokeUserRole([FromBody] RevokeUserRoleDto dto, CancellationToken cancellationToken)
        {
            await _userAssignments.RevokeRoleAsync(dto, cancellationToken);
            return ApiResponse.Success("تم إلغاء إسناد الدور بنجاح.");
        }

        [HttpGet("[action]/{userId}")]
        [AuthorizePermission(MilitaryHealthPermissions.UserClinics.Manage)]
        public async Task<IResult> GetUserClinicAssignments(
            string userId,
            [FromQuery] bool activeOnly = false,
            CancellationToken cancellationToken = default)
        {
            var assignments = await _userClinicAssignments.GetUserClinicAssignmentsAsync(
                userId, activeOnly, cancellationToken);
            return ApiResponse.Success("تم جلب إسنادات عيادات المستخدم بنجاح.", assignments);
        }

        [HttpPost("[action]")]
        [AuthorizePermission(MilitaryHealthPermissions.UserClinics.Manage)]
        public async Task<IResult> AssignUserClinic(
            [FromBody] AssignUserClinicDto dto,
            CancellationToken cancellationToken = default)
        {
            var result = await _userClinicAssignments.AssignAsync(dto, cancellationToken);
            return ApiResponse.Success("تم إسناد الطبيب إلى العيادة بنجاح.", result);
        }

        [HttpPost("[action]")]
        [AuthorizePermission(MilitaryHealthPermissions.UserClinics.Manage)]
        public async Task<IResult> RevokeUserClinic(
            [FromBody] RevokeUserClinicDto dto,
            CancellationToken cancellationToken = default)
        {
            await _userClinicAssignments.RevokeAsync(dto, cancellationToken);
            return ApiResponse.Success("تم إلغاء إسناد الطبيب عن العيادة بنجاح.");
        }
    }
}
