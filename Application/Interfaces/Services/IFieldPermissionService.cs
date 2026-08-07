using Application.Common.Results;
using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

public interface IFieldPermissionService
{
    Task<PaginatedList<FieldPermissionDto>> GetFieldPermissionsAsync(int page, int size, FieldPermissionFilterDto? filter = null);
    Task<FieldPermissionDto> GetByIdAsync(Guid id);
    Task<CreateFieldPermissionsResultDto> CreateAsync(CreateFieldPermissionDto dto);
    Task UpdateAsync(Guid id, UpdateFieldPermissionDto dto);
    Task DeleteAsync(Guid id);

    Task<List<EntityInfoDto>> GetEntitiesAsync();
    Task<List<FieldInfoDto>> GetFieldsAsync(string entityName);
    
    Task ChangeAccessTypeAsync(Guid id, ChangeFieldPermissionAccessTypeDto dto);
}
