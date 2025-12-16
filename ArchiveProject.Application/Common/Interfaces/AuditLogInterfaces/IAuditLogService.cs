using ArchiveProject.Application.Common.Results;
using ArchiveProject.Application.ModelsDto.AuditLogDtos;

namespace ArchiveProject.Application.Common.Interfaces.AuditLogInterfaces
{
    public interface IAuditLogService
    {
        Task<Result<AuditLogDetailsDto>> GetByIdAsync(long id);
        Task<Result<PaginatedList<AuditLogListDto>>> GetAllAsync(int pageNumber, int pageSize);
        Task<Result<PaginatedList<AuditLogListDto>>> GetAllFilteredAsync(int pageNumber,int pageSize,AuditLogFilter auditLogFilter);
        Task<Result<AuditLogSummaryDto>> GetSummaryAsync();

    }
}

