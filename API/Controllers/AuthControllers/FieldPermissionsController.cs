using Application.Common.Results;
using Application.Common.Utilities;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        // =====================================================
        // CRUD
        // =====================================================

        [HttpGet("[action]")]
        public async Task<Result<PaginatedList<FieldPermissionDto>>> GetFieldPermissions(
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
            if (!result.Succeeded || result.Value == null)
                return Result<PaginatedList<FieldPermissionDto>>.Failure(result.Errors, null);

            return Result<PaginatedList<FieldPermissionDto>>.Success(result.Value);
        }

        [HttpGet("[action]/{id}")]
        public async Task<Result<FieldPermissionDto>> GetById(Guid id)
        {
            var result = await _fieldPermissionService.GetByIdAsync(id);
            if (!result.Succeeded || result.Value == null)
                return Result<FieldPermissionDto>.Failure(result.Errors, null);

            return Result<FieldPermissionDto>.Success(result.Value);
        }

        [HttpPost("[action]")]
        public async Task<Result<CreateFieldPermissionsResultDto>> Create([FromBody] CreateFieldPermissionDto dto)
        {
            var result = await _fieldPermissionService.CreateAsync(dto);
            if (!result.Succeeded || result.Value == null)
                return Result<CreateFieldPermissionsResultDto>.Failure(result.Errors, null);

            return Result<CreateFieldPermissionsResultDto>.Success(result.Value);
        }

        [HttpPut("[action]/{id}")]
        public async Task<Result<string>> Update(Guid id, [FromBody] UpdateFieldPermissionDto dto)
        {
            var result = await _fieldPermissionService.UpdateAsync(id, dto);
            if (!result.Succeeded || result.Value == null)
                return Result<string>.Failure(result.Errors, null);

            return Result<string>.Success(result.Value);
        }

        [HttpDelete("[action]/{id}")]
        public async Task<Result<string>> Delete(Guid id)
        {
            var result = await _fieldPermissionService.DeleteAsync(id);
            if (!result.Succeeded || result.Value == null)
                return Result<string>.Failure(result.Errors, null);

            return Result<string>.Success(result.Value);
        }

        [HttpPatch("[action]/{id}")]
        public async Task<Result<string>> ChangeAccessType(Guid id, [FromBody] ChangeFieldPermissionAccessTypeDto dto)
        {
            var result = await _fieldPermissionService.ChangeAccessTypeAsync(id, dto);
            if (!result.Succeeded || result.Value == null)
                return Result<string>.Failure(result.Errors, null);

            return Result<string>.Success(result.Value);
        }

        // =====================================================
        // Reflection: Entities & Fields
        // =====================================================

        [HttpGet("[action]")]
        public async Task<Result<List<EntityInfoDto>>> GetEntities()
        {
            var result = await _fieldPermissionService.GetEntitiesAsync();
            if (!result.Succeeded || result.Value == null)
                return Result<List<EntityInfoDto>>.Failure(result.Errors, null);

            return Result<List<EntityInfoDto>>.Success(result.Value);
        }

        [HttpGet("[action]/{entityName}")]
        public async Task<Result<List<FieldInfoDto>>> GetFields(string entityName)
        {
            var result = await _fieldPermissionService.GetFieldsAsync(entityName);
            if (!result.Succeeded || result.Value == null)
                return Result<List<FieldInfoDto>>.Failure(result.Errors, null);

            return Result<List<FieldInfoDto>>.Success(result.Value);
        }

        [HttpGet("[action]")]
        public Task<Result<List<FieldAccessTypeDto>>> GetAccessTypes()
        {
            var list = Enum.GetValues(typeof(FieldAccessType))
                           .Cast<FieldAccessType>()
                           .Select(e => new FieldAccessTypeDto
                           {
                               Value = (int)e,
                               Name = EnumHelper.GetEnumDescription(e)
                           })
                           .ToList();

            return Task.FromResult(Result<List<FieldAccessTypeDto>>.Success(list));
        }
    }
}