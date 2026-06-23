using Application.Interfaces.Services;
using Application.Common.Results;
using Application.Dtos.AuthDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.AuthControllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class RolesController : ControllerBase
    {
        private readonly IRoleService _roleService;

        public RolesController(IRoleService roleService)
        {
            _roleService = roleService;
        }

        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Create")]
        public async Task<Result<RoleDto>> Create(CreateRoleDto newRole)
        {
            var addRole = await _roleService.CreateRoleAsync(newRole);

            if (addRole.Value == null || !addRole.Succeeded)
            {
                return Result<RoleDto>.Failure(addRole.Errors, null);
            }

            return Result<RoleDto>.Success(addRole.Value);
        }

        [HttpPut("[action]/{id}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<string>> Update(string id, UpdateRoleDto updatedRole)
        {
            var role = await _roleService.UpdateRoleAsync(id, updatedRole);

            if (role.Value == null || !role.Succeeded)
            {
                return Result<string>.Failure(role.Errors, null);
            }
            return Result<string>.Success(role.Value); ;
        }

        [HttpDelete("[action]/{id}")]
        [AuthorizePermission("UserManagement.Delete")]
        public async Task<Result<string>> Delete(string id)
        {
            var deleteRole = await _roleService.DeleteRoleAsync(id);

            if (deleteRole.Value == null || !deleteRole.Succeeded)
            {
                return Result<string>.Failure(deleteRole.Errors, null);
            }
            return Result<string>.Success(deleteRole.Value);
        }

        [HttpGet("[action]/{id}")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<RoleDto>> GetById(string id)
        {
            var res = await _roleService.GetById(id);

            if (res.Value == null || !res.Succeeded)
            {
                return Result<RoleDto>.Failure(res.Errors, null);
            }

            return Result<RoleDto>.Success(res.Value);
        }

        [HttpGet("[action]")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<PaginatedList<RoleDetailsDto>>> GetRolesWithDetails(
            [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
            [FromQuery][Range(1, 500)] int pageSize = 10,
            [FromQuery] string? name = null,
            [FromQuery] bool? isActive = null)
        {
            var rolesResult = await _roleService.GetRolesWithDetailsAsync(pageNumber, pageSize, name, isActive);

            if (!rolesResult.Succeeded || rolesResult.Value == null)
                return Result<PaginatedList<RoleDetailsDto>>.Failure(rolesResult.Errors, null);

            return Result<PaginatedList<RoleDetailsDto>>.Success(rolesResult.Value);
        }

        //[HttpPost("[action]")]
        //[AuthorizePermission("UserManagement.Create")]
        //public async Task<Result<string>> TogglePermissionsForRole([FromBody] AssignPermissionsDto dto)
        //{
        //    var res = await _roleService.TogglePermissionsForRoleAsync(dto);

        //    if (res == null || !res.Succeeded || res.Value == null)
        //        return Result<string>.Failure(res.Errors, null);

        //    return Result<string>.Success(res.Value);
        //}
        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Create")]
        public async Task<Result<string>> SyncPermissionsForRole([FromBody] AssignPermissionsDto dto)
        {
            var res = await _roleService.SyncPermissionsForRoleAsync(dto);
            if (!res.Succeeded) return Result<string>.Failure(res.Errors, null);
            return Result<string>.Success(res.Value);
        }


        [HttpPost("[action]")]
        [AuthorizePermission("UserManagement.Create")]
        public async Task<Result<RoleDetailsDto>> CreateRoleWithPermissions([FromBody] CreateRoleWithPermissionsDto dto)
        {
            var result = await _roleService.CreateRoleWithPermissionsAsync(dto);

            if (!result.Succeeded)
                return Result<RoleDetailsDto>.Failure(result.Errors, null);

            return Result<RoleDetailsDto>.Success(result.Value);
        }

        [HttpGet("[action]")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<string>> GetActiveRolesCount()
        {
            var result = await _roleService.GetActiveRolesCount();

            if (!result.Succeeded || result.Value == null)
                return Result<string>.Failure(result.Errors, null);

            return Result<string>.Success(result.Value);
        }

        [HttpGet("[action]")]
        [AuthorizePermission("UserManagement.Read")]
        public async Task<Result<List<DropdownRoleDto>>> GetDropdownRoles()
        {
            var result = await _roleService.GetDropdownRolesAsync();

            if (!result.Succeeded || result.Value == null)
                return Result<List<DropdownRoleDto>>.Failure(result.Errors, null);

            return Result<List<DropdownRoleDto>>.Success(result.Value);
        }

        [HttpPatch("[action]/{roleId}")]
        [AuthorizePermission("UserManagement.Edit")]
        public async Task<Result<RoleStatusUpdateDto>> ToggleRoleStatus(Guid roleId)
        {
            var result = await _roleService.ToggleRoleStatusAsync(roleId);

            if (!result.Succeeded || result.Value == null)
                return Result<RoleStatusUpdateDto>.Failure(result.Errors, null);

            return Result<RoleStatusUpdateDto>.Success(result.Value);
        }


    }
}

