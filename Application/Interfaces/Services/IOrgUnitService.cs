using Application.Common.Results;
using Application.Dtos.CoreDtos;

namespace Application.Interfaces.Services;

public interface IOrgUnitService
{
    Task<OrgUnitDto> GetByIdAsync(Guid id);
    Task<PaginatedList<OrgUnitDto>> GetListAsync(int pageNumber, int pageSize, string? q = null, string? status = null);
    Task<IReadOnlyList<GuidDropDownDto>> GetDropdownAsync();
    Task<IReadOnlyList<OrgUnitTreeNodeDto>> GetTreeAsync(string? status = null);
    Task<IReadOnlyList<OrgUnitDto>> GetChildrenAsync(Guid parentId);
    Task<IReadOnlyList<OrgUnitDto>> GetDescendantsAsync(Guid parentId);
    Task<OrgUnitDto> CreateAsync(CreateOrgUnitDto dto);
    Task UpdateAsync(Guid id, UpdateOrgUnitDto dto);
    Task SetStatusAsync(Guid id, string status);
    Task DeleteAsync(Guid id);
}
