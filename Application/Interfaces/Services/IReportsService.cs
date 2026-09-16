using Application.Dtos.ExportDtos;
using Application.ModelsDto.AuditLogDtos;

namespace Application.Interfaces.Services
{
    public interface IReportsService
    {
        Task<ExportFileDto> ExportAuditLogsAsync(AuditLogFilter filter, string format);
        Task<ExportFileDto> ExportDashboardAsync(DashboardFilter filter, string format);
    }
}
