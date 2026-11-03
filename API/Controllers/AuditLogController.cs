using API.Infrastructure;
using Application.Common.Permissions;
using Application.Interfaces.Services;
using Application.ModelsDto.AuditLogDtos;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers;

/// <summary>
/// استعراض سجل التدقيق للمدقق (FR-077). لا مسارات تعديل/حذف — AuditLog.Modify مرفوض تصميمياً.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class AuditLogController : ControllerBase
{
    private readonly IAuditLogService _service;

    public AuditLogController(IAuditLogService service)
    {
        _service = service;
    }

    [HttpGet("[action]/{id}")]
    [AuthorizePermission(SystemPermissions.AuditLog.Read)]
    public async Task<IResult> Get(long id)
    {
        var result = await _service.GetByIdAsync(id);
        return ApiResponse.Success("تم جلب تفاصيل سجل التدقيق بنجاح.", result);
    }

    [HttpGet("[action]")]
    [AuthorizePermission(SystemPermissions.AuditLog.Read)]
    public async Task<IResult> GetAuditEntities()
    {
        var entityNames = await _service.GetEntityTypeNamesAsync();
        return ApiResponse.Success("تم جلب أسماء كيانات السجل بنجاح.", entityNames);
    }

    /// <summary>قائمة مُصفّاة عبر query (مستخدم/عملية/مركز/فترة/كيان).</summary>
    [HttpGet("search")]
    [AuthorizePermission(SystemPermissions.AuditLog.Read)]
    public async Task<IResult> Search(
        [FromQuery] string? userName = null,
        [FromQuery] string? actorUserId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] Guid? centerId = null,
        [FromQuery] string? entityType = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10)
    {
        var filter = new AuditLogFilter
        {
            userName = userName,
            actorUserId = actorUserId,
            eventType = eventType,
            centerId = centerId,
            entityType = entityType,
            From = from,
            To = to
        };

        var result = await _service.GetAllFilteredAsync(pageNumber, pageSize, filter);
        var message = result.TotalCount == 0
            ? "لا توجد سجلات مطابقة للتصفية."
            : "تم جلب سجلات التدقيق بنجاح.";
        return ApiResponse.Success(message, result);
    }

    [HttpPost("[action]")]
    [AuthorizePermission(SystemPermissions.AuditLog.Read)]
    public async Task<IResult> GetAllFiltered(
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        [FromForm] AuditLogFilter? filter = null)
    {
        filter ??= new AuditLogFilter();
        var result = await _service.GetAllFilteredAsync(pageNumber, pageSize, filter);
        var message = result.TotalCount == 0
            ? "لا توجد سجلات مطابقة للتصفية."
            : "تم جلب سجلات التدقيق بنجاح.";
        return ApiResponse.Success(message, result);
    }

    [HttpGet("[action]")]
    [AuthorizePermission(SystemPermissions.AuditLog.Read)]
    public async Task<IResult> GetAuditLogSummary()
    {
        var result = await _service.GetSummaryAsync();
        return ApiResponse.Success("تم جلب ملخص سجل التدقيق بنجاح.", result);
    }
}
