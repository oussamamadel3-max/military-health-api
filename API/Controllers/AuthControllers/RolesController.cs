using API.Infrastructure;
using Application.Common.Permissions;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Domain.Enums;
using API.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.AuthControllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class RolesController : ControllerBase
    {
        private readonly IRoleService _roleService;
        private readonly IRoleAdminOperationsService _adminOperations;

        public RolesController(IRoleService roleService, IRoleAdminOperationsService adminOperations)
        {
            _roleService = roleService;
            _adminOperations = adminOperations;
        }

        [HttpPost("[action]")]
        [AuthorizePermission(SystemPermissions.RoleManagement.Create)]
        public async Task<IResult> Create([FromBody] CreateRoleDto newRole)
        {
            var addRole = await _roleService.CreateRoleAsync(newRole);
            return ApiResponse.Success("تم إضافة الدور بنجاح.", addRole);
        }

        [HttpPut("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.RoleManagement.Edit)]
        public async Task<IResult> Update(string id, [FromBody] UpdateRoleDto updatedRole)
        {
            await _roleService.UpdateRoleAsync(id, updatedRole);
            return ApiResponse.Success("تم تحديث الدور بنجاح.");
        }

        [HttpDelete("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.RoleManagement.Delete)]
        public async Task<IResult> Delete(string id)
        {
            await _roleService.DeleteRoleAsync(id);
            return ApiResponse.Success("تم حذف الدور بنجاح.");
        }

        [HttpGet("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.RoleManagement.Read)]
        public async Task<IResult> GetById(string id)
        {
            var role = await _roleService.GetById(id);
            return ApiResponse.Success("تم جلب بيانات الدور بنجاح.", role);
        }

        /// <summary>
        /// آخر العمليات الإدارية على الدور (Read Model فوق AuditEvent).
        /// يتطلب AuditLog.Read.
        /// </summary>
        [HttpGet("[action]/{roleId}")]
        [AuthorizePermission(SystemPermissions.AuditLog.Read)]
        public async Task<IResult> GetAdminOperations(
            string roleId,
            [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
            [FromQuery][Range(1, 100)] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            var result = await _adminOperations.GetForRoleAsync(roleId, pageNumber, pageSize, cancellationToken);
            var message = result.TotalCount == 0
                ? "لا توجد عمليات إدارية مسجّلة لهذا الدور."
                : "تم جلب آخر العمليات الإدارية للدور بنجاح.";
            return ApiResponse.Success(message, result);
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.RoleManagement.Read)]
        public async Task<IResult> GetRolesWithDetails(
            [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
            [FromQuery][Range(1, 500)] int pageSize = 10,
            [FromQuery] string? name = null,
            [FromQuery] bool? isActive = null,
            [FromQuery] RoleScope? scope = null)
        {
            var rolesResult = await _roleService.GetRolesWithDetailsAsync(pageNumber, pageSize, name, isActive, scope);
            return ApiResponse.Success("تم جلب قائمة الأدوار بنجاح.", rolesResult);
        }

        [HttpPost("[action]")]
        [AuthorizePermission(SystemPermissions.RoleManagement.AssignPermissions)]
        public async Task<IResult> SyncPermissionsForRole([FromBody] AssignPermissionsDto dto)
        {
            var summary = await _roleService.SyncPermissionsForRoleAsync(dto);
            return ApiResponse.Success(summary.Message, summary);
        }

        [HttpPost("[action]")]
        [AuthorizePermission(SystemPermissions.RoleManagement.Create, SystemPermissions.RoleManagement.AssignPermissions)]
        public async Task<IResult> CreateRoleWithPermissions([FromBody] CreateRoleWithPermissionsDto dto)
        {
            var result = await _roleService.CreateRoleWithPermissionsAsync(dto);
            return ApiResponse.Success("تم إنشاء الدور مع الصلاحيات بنجاح.", result);
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.RoleManagement.Read)]
        public async Task<IResult> GetActiveRolesCount()
        {
            var count = await _roleService.GetActiveRolesCount();
            return ApiResponse.Success("تم جلب عدد الأدوار النشطة.", count);
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.RoleManagement.Read)]
        [EnableRateLimiting(RateLimitPolicies.Administrative)]
        public async Task<IResult> GetDropdownRoles()
        {
            var roles = await _roleService.GetDropdownRolesAsync();
            return ApiResponse.Success("تم جلب قائمة الأدوار المنسدلة.", roles);
        }

        [HttpPatch("[action]/{roleId}")]
        [AuthorizePermission(SystemPermissions.RoleManagement.ChangeStatus)]
        public async Task<IResult> ToggleRoleStatus(Guid roleId)
        {
            var result = await _roleService.ToggleRoleStatusAsync(roleId);
            return ApiResponse.Success("تم تعديل حالة الدور بنجاح.", result);
        }
    }
}
