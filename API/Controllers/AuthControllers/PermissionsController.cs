using API.Infrastructure;
using Application.Common.Permissions;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.AuthControllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PermissionsController : ControllerBase
    {
        private readonly IPermissionService _permissionService;

        public PermissionsController(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        [HttpGet("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.PermissionManagement.Read)]
        public async Task<IResult> GetById(string id)
        {
            var permission = await _permissionService.GetByIdAsync(id);
            return ApiResponse.Success("تم جلب بيانات الصلاحية بنجاح.", permission);
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.PermissionManagement.Read)]
        public async Task<IResult> GetPermissions()
        {
            var result = await _permissionService.GetPermissionsAsync();
            return ApiResponse.Success("تم جلب قائمة الصلاحيات بنجاح.", result);
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.PermissionManagement.Read)]
        public async Task<IResult> GetPermissionModules()
        {
            var result = await _permissionService.GetPermissionModulesAsync();
            return ApiResponse.Success("تم جلب وحدات الصلاحيات بنجاح.", result);
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.PermissionManagement.Read)]
        public async Task<IResult> GetPermissionsWithRoles([FromQuery] string? name = null, [FromQuery] bool? isActive = null)
        {
            var result = await _permissionService.GetPermissionsWithRolesAsync(name, isActive);
            return ApiResponse.Success("تم جلب الصلاحيات مع الأدوار بنجاح.", result);
        }

        [HttpPut("[action]/{roleId}/Roles/{permissionId}")]
        [AuthorizePermission(SystemPermissions.RoleManagement.AssignPermissions)]
        public async Task<IResult> TogglePermissionForRole(string roleId, Guid permissionId)
        {
            await _permissionService.TogglePermissionForRoleAsync(roleId, permissionId);
            return ApiResponse.Success("تم تعديل حظر/سماح الصلاحية للدور بنجاح.");
        }

        [HttpPut("[action]/{permissionId}")]
        [AuthorizePermission(SystemPermissions.PermissionManagement.ChangeStatus)]
        public async Task<IResult> TogglePermissionStatus(Guid permissionId)
        {
            await _permissionService.TogglePermissionStatusAsync(permissionId);
            return ApiResponse.Success("تم تغيير حالة الصلاحية بنجاح.");
        }
    }
}
