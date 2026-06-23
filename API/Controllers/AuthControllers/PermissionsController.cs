using Application.Interfaces.Services;
using Application.Dtos.AuthDtos;
using Microsoft.AspNetCore.Authorization;
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

        //[HttpPost("[action]")]
        ////[AuthorizePermission("Permission.Create")]
        //public async Task<Result<PermissionDto>> Create(CreatePermissionDto permission)
        //{
        //    var addPermission = await _permissionService.CreatePermissionAsync(permission);

        //    if (addPermission.Value == null || !addPermission.Succeeded)
        //    {
        //        return Result<PermissionDto>.Failure(addPermission.Errors, null);
        //    }
        //    return Result<PermissionDto>.Success(addPermission.Value);
        //}

        //[HttpPut("[action]/{id}")]
        ////[AuthorizePermission("Permission.Edit")]
        //public async Task<Result<string>> Update(Guid id, UpdatePermissionDto permission)
        //{

        //    var permissions = await _permissionService.UpdatePermissionAsync(id, permission);

        //    if (permissions.Value == null || !permissions.Succeeded)
        //    {
        //        return Result<string>.Failure(permissions.Errors, null);
        //    }
        //    return Result<string>.Success(permissions.Value);
        //}

        //[HttpDelete("[action]/{id}")]
        ////[AuthorizePermission("Permission.Delete")]
        //public async Task<Result<string>> Delete(string id)
        //{
        //    var deletePermission = await _permissionService.DeletePermissionAsync(id);

        //    if (deletePermission.Value == null || !deletePermission.Succeeded)
        //    {
        //        return Result<string>.Failure(deletePermission.Errors, null);
        //    }
        //    return Result<string>.Success(deletePermission.Value);
        //}

        [HttpGet("[action]/{id}")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<PermissionDto>> GetById(string id)
        {
            var permission = await _permissionService.GetByIdAsync(id);

            if (permission.Value == null || !permission.Succeeded)
            {
                return Result<PermissionDto>.Failure(permission.Errors, null);
            }
            return Result<PermissionDto>.Success(permission.Value);
        }

        [HttpGet("[action]")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<List<PermissionDto>>> GetPermissions()
        {
            var result = await _permissionService.GetPermissionsAsync();

            if (!result.Succeeded || result.Value is null)
                return Result<List<PermissionDto>>.Failure(result.Errors, null);

            return Result<List<PermissionDto>>.Success(result.Value);
        }


        [HttpGet("[action]")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<List<PermissionWithRolesResultDto>>> GetPermissionsWithRoles([FromQuery] string? name = null, [FromQuery] bool? isActive = null)
        {
            var result = await _permissionService.GetPermissionsWithRolesAsync(name, isActive);

            if (!result.Succeeded || result.Value is null)
                return Result<List<PermissionWithRolesResultDto>>.Failure(result.Errors, null);

            return Result<List<PermissionWithRolesResultDto>>.Success(result.Value);
        }



        [HttpPut("[action]/{roleId}/Roles/{permissionId}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<string>> TogglePermissionForRole(string roleId, Guid permissionId)
        {
            var result = await _permissionService.TogglePermissionForRoleAsync(roleId, permissionId);

            if (!result.Succeeded || result.Value is null)
                return Result<string>.Failure(result.Errors, null);

            return Result<string>.Success(result.Value);
        }

        [HttpPut("[action]/{permissionId}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<string>> TogglePermissionStatus(Guid permissionId)
        {
            var result = await _permissionService.TogglePermissionStatusAsync(permissionId);

            if (!result.Succeeded || result.Value is null)
                return Result<string>.Failure(result.Errors, null);

            return Result<string>.Success(result.Value);
        }



    }
}
