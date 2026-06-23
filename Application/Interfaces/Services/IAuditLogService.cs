using Application.Common.Results;
using Application.ModelsDto.AuditLogDtos;

namespace Application.Interfaces.Services
{
    public interface IAuditLogService
    {
        Task<Result<AuditLogDetailsDto>> GetByIdAsync(long id);
        Task<Result<PaginatedList<AuditLogListDto>>> GetAllAsync(int pageNumber, int pageSize);
        Task<Result<PaginatedList<AuditLogListDto>>> GetAllFilteredAsync(int pageNumber,int pageSize,AuditLogFilter auditLogFilter);
        Task<Result<AuditLogSummaryDto>> GetSummaryAsync();

    }
}

