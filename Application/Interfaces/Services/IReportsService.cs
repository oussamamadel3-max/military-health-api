using Application.Dtos.DocumentDtos;
using Application.Dtos.ExportDtos;
using Application.ModelsDto.AuditLogDtos;

namespace Application.Interfaces.Services
{
    public interface IReportsService
    {
        Task<ExportFileDto> ExportDocumentsAsync(DocumentSearchCriteria criteria, string format);
        Task<ExportFileDto> ExportAuditLogsAsync(AuditLogFilter filter, string format);
        Task<ExportFileDto> ExportDashboardAsync(DashboardFilter filter, string format);
    }
}
