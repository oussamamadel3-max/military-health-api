using API.Infrastructure;
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
    public async Task<IResult> Get(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return ApiResponse.Success("تم جلب تفاصيل سجل التدقيق بنجاح.", result);
    }

    [HttpGet("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<IResult> GetAuditEntities()
    {
        var entityNames = await _auditLogRepository.GetEntityTypeNamesAsync();
        return ApiResponse.Success("تم جلب أسماء كيانات السجل بنجاح.", entityNames.ToList());
    }

    [HttpPost("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<IResult> GetAllFiltered(
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        [FromForm] AuditLogFilter? filter = null)
    {
        filter ??= new AuditLogFilter();
        var result = await _service.GetAllFilteredAsync(pageNumber, pageSize, filter);
        return ApiResponse.Success("تم جلب سجلات التدقيق بنجاح.", result);
    }

    [HttpGet("[action]")]
    [AuthorizePermission("AuditLog.Read")]
    public async Task<IResult> GetAuditLogSummary()
    {
        var result = await _service.GetSummaryAsync();
        return ApiResponse.Success("تم جلب ملخص سجل التدقيق بنجاح.", result);
    }
}
