using Application.Common.Results;
using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

/// <summary>قراءة آخر العمليات الإدارية المرتبطة بدور مستهدف.</summary>
public interface IRoleAdminOperationsService
{
    Task<PaginatedList<UserAdminOperationDto>> GetForRoleAsync(
        string roleId,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}
