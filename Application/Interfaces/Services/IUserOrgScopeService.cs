using Application.Common.Results;
using Application.Dtos.CoreDtos;

namespace Application.Interfaces.Services;

public interface IUserOrgScopeService
{
    Task<UserOrgScopeDto> GetByIdAsync(Guid id);
    Task<PaginatedList<UserOrgScopeDto>> GetListAsync(int pageNumber, int pageSize, string? q = null, string? status = null);
    Task<IReadOnlyList<UserOrgScopeDto>> GetByUserIdAsync(string userId);
    Task<IReadOnlyList<UserOrgScopeDto>> GetMyScopesAsync();
    Task<IReadOnlyList<AccessibleOrgUnitDto>> GetMyAccessibleOrgUnitsAsync(string permissionName);
    Task<UserOrgScopeDto> CreateAsync(CreateUserOrgScopeDto dto);
    Task UpdateAsync(Guid id, UpdateUserOrgScopeDto dto);
    Task DeleteAsync(Guid id);
}
