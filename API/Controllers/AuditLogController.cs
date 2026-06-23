using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.ModelsDto.AuditLogDtos;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

[Route("api/[controller]")]
[ApiController]
public class AuditLogController : ControllerBase
{
    private readonly IAuditLogService _service;
    private readonly IAuditLogRepository _auditLogRepository;

    public AuditLogController(IAuditLogService service, IAuditLogRepository auditLogRepository)
    {
        _service = service;
        _auditLogRepository = auditLogRepository;
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
    public async Task<ActionResult<List<string>>> GetAuditEntities()
    {
        var entityNames = await _auditLogRepository.GetEntityTypeNamesAsync();
        return Ok(entityNames);
    }


    [HttpPost("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<IActionResult> GetAllFiltered(
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        [FromForm] AuditLogFilter? filter = null)
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

