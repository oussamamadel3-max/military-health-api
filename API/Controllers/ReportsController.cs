using Application.Dtos.DocumentDtos;
using Application.Interfaces.Services;
using Application.ModelsDto.AuditLogDtos;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly IReportsService _reports;
        public ReportsController(IReportsService reports) => _reports = reports;

        // POST api/reports/documents/export?format=excel
        [HttpPost("documents/export")]
        [AuthorizePermission("ReportsDocuments.Export")]
        
        public async Task<IActionResult> ExportDocuments(
            [FromForm] DocumentSearchCriteria criteria,
            [FromQuery][RegularExpression(@"^(?i)(excel|pdf)$", ErrorMessage = "صيغة التصدير يجب أن تكون excel أو pdf.")] string format = "excel")
        {
            var res = await _reports.ExportDocumentsAsync(criteria, format);
            return File(res.FileContents, res.ContentType, res.FileName);
        }

        // POST api/reports/auditlogs/export?format=pdf
        [HttpPost("auditlogs/export")]
        [AuthorizePermission("ReportsAuditLogs.Export")]
        public async Task<IActionResult> ExportAudit(
            [FromForm] AuditLogFilter filter,
            [FromQuery][RegularExpression(@"^(?i)(excel|pdf)$", ErrorMessage = "صيغة التصدير يجب أن تكون excel أو pdf.")] string format = "excel")
        {
            var res = await _reports.ExportAuditLogsAsync(filter, format);
            return File(res.FileContents, res.ContentType, res.FileName);
        }

        // POST api/reports/dashboard/export?format= 
        [HttpPost("dashboard/export")]
        [AuthorizePermission("ReportsDashboard.Export")]
        public async Task<IActionResult> ExportDashboard(
            [FromForm] DashboardFilter filter,
            [FromQuery][RegularExpression(@"^(?i)(excel|pdf)$", ErrorMessage = "صيغة التصدير يجب أن تكون excel أو pdf.")] string format = "excel")
        {
            var res = await _reports.ExportDashboardAsync(filter, format);
            return File(res.FileContents, res.ContentType, res.FileName);
        }
    }
}
