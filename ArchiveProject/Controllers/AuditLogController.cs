using ArchiveProject.Application.Common.Interfaces.AuditLogInterfaces;
using ArchiveProject.Application.ModelsDto.AuditLogDtos;
using ArchiveProject.Domain.Common;
using ArchiveProject.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Route("api/[controller]")]
[ApiController]
public class AuditLogController : ControllerBase
{
    private readonly IAuditLogService _service;
    private readonly MainDbContext _context;

    public AuditLogController(IAuditLogService service, MainDbContext context)
    {
        _service = service;
        _context = context;
    }

    // استرجاع سجل حسب المعرف
    [HttpGet("[action]/{id}")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<IActionResult> Get(long id)
    {
        var result = await _service.GetByIdAsync(id);
        if (!result.Succeeded || result.Value == null)
            return NotFound(new { Errors = result.Errors });

        return Ok(result.Value);
    }

    [HttpGet("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public ActionResult<List<string>> GetAuditEntities()
    {
        // جلب فقط الكيانات التي ترث من BaseEntity
        var entityNames = _context.Model.GetEntityTypes()
            .Where(et => et.ClrType != null
                    && et.GetTableName() != null
                    && typeof(BaseEntity).IsAssignableFrom(et.ClrType)
                    && !et.ClrType.IsAbstract) // استبعاد BaseEntity نفسه
            .Select(et => et.ClrType.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        return Ok(entityNames);
    }


    [HttpPost("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<IActionResult> GetAllFiltered([FromQuery] int pageNumber = 1,[FromQuery] int pageSize = 10,[FromForm] AuditLogFilter filter = null!)
    {
        filter ??= new AuditLogFilter();

        var result = await _service.GetAllFilteredAsync(pageNumber, pageSize, filter);

        if (!result.Succeeded || result.Value == null)
            return BadRequest(new { Errors = result.Errors });

        return Ok(result.Value);
    }



    [HttpGet("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<ActionResult<Result<AuditLogSummaryDto>>> GetAuditLogSummary()
    {
        var result = await _service.GetSummaryAsync();
        if (result.Failed)
            return BadRequest(result.Errors);

        return Ok(result);
    }


}

