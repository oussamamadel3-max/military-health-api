using Application.Interfaces.Services;
using Application.Dtos.DocumentDtos;
using Application.Dtos.ExportDtos;
using Application.ModelsDto.AuditLogDtos;

namespace Application.Interfaces.Services
{
    public interface IReportsService
    {
        Task<Result<ExportFileDto>> ExportDocumentsAsync(DocumentSearchCriteria criteria, string format); // "excel" | "pdf"
        Task<Result<ExportFileDto>> ExportAuditLogsAsync(AuditLogFilter filter, string format);
        Task<Result<ExportFileDto>> ExportDashboardAsync(DashboardFilter filter, string format);
    }
}
