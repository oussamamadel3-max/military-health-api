using ArchiveProject.Application.Common.Interfaces.DashboardInterfaces;
using ArchiveProject.Application.Common.Interfaces.Reports;
using ArchiveProject.Application.Dtos.DocumentDtos;
using ArchiveProject.Application.ModelsDto.AuditLogDtos;
using Microsoft.AspNetCore.Mvc;

namespace ArchiveProject.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly IReportsService _reports;
        public ReportsController(IReportsService reports) => _reports = reports;

        // POST api/reports/documents/export?format=excel
        [HttpPost("documents/export")]
        public async Task<IActionResult> ExportDocuments([FromForm] DocumentSearchCriteria criteria, [FromQuery] string format = "excel")
        {
            var res = await _reports.ExportDocumentsAsync(criteria, format);
            if (!res.Succeeded || res.Value == null) return BadRequest(res);
            return File(res.Value.FileContents, res.Value.ContentType, res.Value.FileName);
        }


        // POST api/reports/auditlogs/export?format=pdf
        [HttpPost("auditlogs/export")]
        public async Task<IActionResult> ExportAudit([FromForm] AuditLogFilter filter,[FromQuery] string format = "excel")
        {
            var res = await _reports.ExportAuditLogsAsync(filter, format);
            if (!res.Succeeded || res.Value == null) return BadRequest(res);
            return File(res.Value.FileContents, res.Value.ContentType, res.Value.FileName);
        }


        // POST api/reports/dashboard/export?format= 
        [HttpPost("dashboard/export")]
        public async Task<IActionResult> ExportDashboard([FromForm] DashboardFilter filter, [FromQuery] string format = "excel")
        {
            var res = await _reports.ExportDashboardAsync(filter, format);
            if (!res.Succeeded || res.Value == null) return BadRequest(res);
            return File(res.Value.FileContents, res.Value.ContentType, res.Value.FileName);
        }

    }
}
