using Application.Common.Results;
using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

public interface IFieldPermissionService
{
    Task<Result<PaginatedList<FieldPermissionDto>>> GetFieldPermissionsAsync(int page, int size, FieldPermissionFilterDto? filter = null);
    Task<Result<FieldPermissionDto>> GetByIdAsync(Guid id);
    Task<Result<CreateFieldPermissionsResultDto>> CreateAsync(CreateFieldPermissionDto dto);
    Task<Result<string>> UpdateAsync(Guid id, UpdateFieldPermissionDto dto);
    Task<Result<string>> DeleteAsync(Guid id);

    Task<Result<List<EntityInfoDto>>> GetEntitiesAsync();
    Task<Result<List<FieldInfoDto>>> GetFieldsAsync(string entityName);
    
    Task<Result<string>> ChangeAccessTypeAsync(Guid id, ChangeFieldPermissionAccessTypeDto dto);
}

