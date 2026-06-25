using Application.Common.Results;
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

    [HttpGet("[action]/{id}")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<Result<AuditLogDetailsDto>> Get(long id)
        => await _service.GetByIdAsync(id);

    [HttpGet("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<Result<List<string>>> GetAuditEntities()
    {
        var entityNames = await _auditLogRepository.GetEntityTypeNamesAsync();
        return Result<List<string>>.Success(entityNames.ToList());
    }

    [HttpPost("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<Result<PaginatedList<AuditLogListDto>>> GetAllFiltered(
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        [FromForm] AuditLogFilter? filter = null)
    {
        filter ??= new AuditLogFilter();
        return await _service.GetAllFilteredAsync(pageNumber, pageSize, filter);
    }

    [HttpGet("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<Result<AuditLogSummaryDto>> GetAuditLogSummary()
        => await _service.GetSummaryAsync();
}
