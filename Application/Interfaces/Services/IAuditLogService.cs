using Application.Common.Results;
using Application.ModelsDto.AuditLogDtos;

namespace Application.Interfaces.Services
{
    public interface IAuditLogService
    {
        Task<AuditLogDetailsDto> GetByIdAsync(long id);
        Task<PaginatedList<AuditLogListDto>> GetAllAsync(int pageNumber, int pageSize);
        Task<PaginatedList<AuditLogListDto>> GetAllFilteredAsync(int pageNumber, int pageSize, AuditLogFilter auditLogFilter);
        Task<AuditLogSummaryDto> GetSummaryAsync();
    }
}
