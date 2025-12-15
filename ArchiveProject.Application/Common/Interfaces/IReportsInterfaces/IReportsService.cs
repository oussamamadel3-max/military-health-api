using ArchiveProject.Application.Dtos.ExportDtos;
using ArchiveProject.Application.Dtos.DocumentDtos;
using ArchiveProject.Application.Common.Interfaces.DashboardInterfaces;

namespace ArchiveProject.Application.Common.Interfaces.Reports
{
    public interface IReportsService
    {
        Task<Result<ExportFileDto>> ExportDocumentsAsync(DocumentSearchCriteria criteria, string format); // "excel" | "pdf"
        Task<Result<ExportFileDto>> ExportAuditLogsAsync(DashboardFilter filter, string format);
        Task<Result<ExportFileDto>> ExportDashboardAsync(DashboardFilter filter, string format);
    }
}
