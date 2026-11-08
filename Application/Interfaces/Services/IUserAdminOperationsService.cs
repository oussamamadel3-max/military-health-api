using Application.Common.Results;
using Application.Dtos.AuthDtos;

namespace Application.Interfaces.Services;

/// <summary>قراءة آخر العمليات الإدارية المرتبطة بحساب مستخدم مستهدف.</summary>
public interface IUserAdminOperationsService
{
    Task<PaginatedList<UserAdminOperationDto>> GetForUserAsync(
        string userId,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}
