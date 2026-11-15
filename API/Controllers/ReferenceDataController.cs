using System.ComponentModel.DataAnnotations;
using API.Infrastructure;
using API.RateLimiting;
using Application.Common.Permissions;
using Application.Dtos.ReferenceData;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/reference-data")]
public class ReferenceDataController : ControllerBase
{
    private readonly IReferenceDataService _referenceData;

    public ReferenceDataController(IReferenceDataService referenceData) => _referenceData = referenceData;

    [HttpGet("catalogs")]
    [AuthorizePermission(MilitaryHealthPermissions.System.ReferenceData)]
    [EnableRateLimiting(RateLimitPolicies.ReferenceData)]
    public IResult GetCatalogs()
    {
        var catalogs = _referenceData.GetCatalogs();
        return ApiResponse.Success("تم جلب كتالوجات القوائم المرجعية.", catalogs);
    }

    /// <summary>شاشة الإدارة: يشمل القيم غير الفعالة.</summary>
    [HttpGet("{catalog}")]
    [AuthorizePermission(MilitaryHealthPermissions.System.ReferenceData)]
    [EnableRateLimiting(RateLimitPolicies.ReferenceData)]
    public async Task<IResult> GetAdminList(
        string catalog,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _referenceData.GetAdminListAsync(catalog, pageNumber, pageSize, search, isActive, cancellationToken);
        return ApiResponse.Success("تم جلب القائمة المرجعية بنجاح.", result);
    }

    /// <summary>نماذج الإدخال التشغيلية: القيم الفعالة فقط.</summary>
    [HttpGet("{catalog}/options")]
    [EnableRateLimiting(RateLimitPolicies.ReferenceData)]
    public async Task<IResult> GetActiveOptions(string catalog, CancellationToken cancellationToken = default)
    {
        var result = await _referenceData.GetActiveOptionsAsync(catalog, cancellationToken);
        return ApiResponse.Success("تم جلب خيارات القائمة المرجعية النشطة.", result);
    }

    [HttpGet("{catalog}/{id}")]
    [AuthorizePermission(MilitaryHealthPermissions.System.ReferenceData)]
    [EnableRateLimiting(RateLimitPolicies.ReferenceData)]
    public async Task<IResult> GetById(string catalog, string id, CancellationToken cancellationToken = default)
    {
        var item = await _referenceData.GetByIdAsync(catalog, id, cancellationToken);
        return ApiResponse.Success("تم جلب القيمة المرجعية بنجاح.", item);
    }

    [HttpPost("{catalog}")]
    [AuthorizePermission(MilitaryHealthPermissions.System.ReferenceData)]
    public async Task<IResult> Create(
        string catalog,
        [FromBody] ReferenceDataWriteDto dto,
        CancellationToken cancellationToken = default)
    {
        var created = await _referenceData.CreateAsync(catalog, dto, cancellationToken);
        return ApiResponse.Success("تم إضافة القيمة المرجعية بنجاح.", created);
    }

    [HttpPut("{catalog}/{id}")]
    [AuthorizePermission(MilitaryHealthPermissions.System.ReferenceData)]
    public async Task<IResult> Update(
        string catalog,
        string id,
        [FromBody] ReferenceDataWriteDto dto,
        CancellationToken cancellationToken = default)
    {
        var updated = await _referenceData.UpdateAsync(catalog, id, dto, cancellationToken);
        return ApiResponse.Success("تم تحديث القيمة المرجعية بنجاح.", updated);
    }

    [HttpPost("{catalog}/{id}/set-active")]
    [AuthorizePermission(MilitaryHealthPermissions.System.ReferenceData)]
    public async Task<IResult> SetActive(
        string catalog,
        string id,
        [FromBody] ReferenceDataSetActiveDto dto,
        CancellationToken cancellationToken = default)
    {
        var updated = await _referenceData.SetActiveAsync(catalog, id, dto.IsActive, cancellationToken);
        return ApiResponse.Success(dto.IsActive ? "تم تفعيل القيمة المرجعية بنجاح." : "تم تعطيل القيمة المرجعية بنجاح.", updated);
    }

    [HttpDelete("{catalog}/{id}")]
    [AuthorizePermission(MilitaryHealthPermissions.System.ReferenceData)]
    public IResult Delete(string catalog, string id)
    {
        _ = catalog;
        _ = id;
        throw new ReferenceDataDeleteNotAllowedException();
    }
}
