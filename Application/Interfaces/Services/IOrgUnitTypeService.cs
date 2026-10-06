using Application.Common.Results;
using Application.Dtos.CoreDtos;

namespace Application.Interfaces.Services;

public interface IOrgUnitTypeService
{
    Task<OrgUnitTypeDto> GetByIdAsync(Guid id);
    Task<PaginatedList<OrgUnitTypeDto>> GetListAsync(int pageNumber, int pageSize, string? q = null, bool? isActive = null);
    Task<IReadOnlyList<OrgUnitTypeDto>> GetAllActiveAsync();
    Task<OrgUnitTypeDto> CreateAsync(CreateOrgUnitTypeDto dto);
    Task UpdateAsync(Guid id, UpdateOrgUnitTypeDto dto);
    Task SetActiveAsync(Guid id, bool isActive);
}
