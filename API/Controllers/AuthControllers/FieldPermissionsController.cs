using API.Infrastructure;
using API.RateLimiting;
using Application.Common.Permissions;
using Application.Common.Utilities;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Domain.Enums;
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
    public class FieldPermissionsController : ControllerBase
    {
        private readonly IFieldPermissionService _fieldPermissionService;

        public FieldPermissionsController(IFieldPermissionService fieldPermissionService)
        {
            _fieldPermissionService = fieldPermissionService;
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.FieldPermissionManagement.Read)]
        [EnableRateLimiting(RateLimitPolicies.Administrative)]
        public async Task<IResult> GetFieldPermissions(
            [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
            [FromQuery][Range(1, 500)] int pageSize = 10,
            [FromQuery] string? roleId = null,
            [FromQuery] string? userId = null,
            [FromQuery] string? entityName = null,
            [FromQuery] string? fieldName = null,
            [FromQuery] FieldAccessType? accessType = null)
        {
            var filter = new FieldPermissionFilterDto
            {
                RoleId = roleId,
                UserId = userId,
                EntityName = entityName,
                FieldName = fieldName,
                AccessType = accessType
            };

            var result = await _fieldPermissionService.GetFieldPermissionsAsync(pageNumber, pageSize, filter);
            return ApiResponse.Success("تم جلب صلاحيات الحقول بنجاح.", result);
        }

        [HttpGet("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.FieldPermissionManagement.Read)]
        [EnableRateLimiting(RateLimitPolicies.Administrative)]
        public async Task<IResult> GetById(Guid id)
        {
            var result = await _fieldPermissionService.GetByIdAsync(id);
            return ApiResponse.Success("تم جلب صلاحية الحقل بنجاح.", result);
        }

        [HttpPost("[action]")]
        [AuthorizePermission(SystemPermissions.FieldPermissionManagement.Create)]
        public async Task<IResult> Create([FromBody] CreateFieldPermissionDto dto)
        {
            var result = await _fieldPermissionService.CreateAsync(dto);
            return ApiResponse.Success("تم إنشاء صلاحيات الحقول بنجاح.", result);
        }

        [HttpPut("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.FieldPermissionManagement.Edit)]
        public async Task<IResult> Update(Guid id, [FromBody] UpdateFieldPermissionDto dto)
        {
            await _fieldPermissionService.UpdateAsync(id, dto);
            return ApiResponse.Success("تم تعديل صلاحيات الحقل بنجاح.");
        }

        [HttpDelete("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.FieldPermissionManagement.Delete)]
        public async Task<IResult> Delete(Guid id)
        {
            await _fieldPermissionService.DeleteAsync(id);
            return ApiResponse.Success("تم حذف صلاحية الحقل بنجاح.");
        }

        [HttpPatch("[action]/{id}")]
        [AuthorizePermission(SystemPermissions.FieldPermissionManagement.ChangeAccessType)]
        public async Task<IResult> ChangeAccessType(Guid id, [FromBody] ChangeFieldPermissionAccessTypeDto dto)
        {
            await _fieldPermissionService.ChangeAccessTypeAsync(id, dto);
            return ApiResponse.Success("تم تغيير نوع الوصول بنجاح.");
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.FieldPermissionManagement.Read)]
        [EnableRateLimiting(RateLimitPolicies.Administrative)]
        public async Task<IResult> GetEntities()
        {
            var result = await _fieldPermissionService.GetEntitiesAsync();
            return ApiResponse.Success("تم جلب قائمة الكيانات بنجاح.", result);
        }

        [HttpGet("[action]/{entityName}")]
        [AuthorizePermission(SystemPermissions.FieldPermissionManagement.Read)]
        [EnableRateLimiting(RateLimitPolicies.Administrative)]
        public async Task<IResult> GetFields(string entityName)
        {
            var result = await _fieldPermissionService.GetFieldsAsync(entityName);
            return ApiResponse.Success("تم جلب قائمة الحقول بنجاح.", result);
        }

        [HttpGet("[action]")]
        [AuthorizePermission(SystemPermissions.FieldPermissionManagement.Read)]
        [EnableRateLimiting(RateLimitPolicies.Administrative)]
        public IResult GetAccessTypes()
        {
            var list = Enum.GetValues(typeof(FieldAccessType))
                           .Cast<FieldAccessType>()
                           .Select(e => new FieldAccessTypeDto
                           {
                               Value = (int)e,
                               Name = EnumHelper.GetEnumDescription(e)
                           })
                           .ToList();

            return ApiResponse.Success("تم جلب أنواع الوصول بنجاح.", list);
        }
    }
}
