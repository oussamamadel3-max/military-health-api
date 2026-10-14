using System.ComponentModel.DataAnnotations;
using API.Infrastructure;
using Application.Common.Permissions;
using Application.Dtos.Centers;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// إدارة المراكز الصحية (FR-008..FR-011). مسارات اختيار المركز النشط تبقى في ActiveCenterController تحت api/centers.
/// </summary>
[ApiController]
[Authorize]
[Route("api/centers")]
public class CentersController : ControllerBase
{
    private readonly IMedicalCenterService _centers;

    public CentersController(IMedicalCenterService centers) => _centers = centers;

    [HttpGet]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> GetCenters(
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _centers.GetCentersAsync(pageNumber, pageSize, search, isActive, cancellationToken);
        return ApiResponse.Success("تم جلب قائمة المراكز الصحية بنجاح.", result);
    }

    [HttpGet("{centerId:guid}")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> GetById(Guid centerId, CancellationToken cancellationToken = default)
    {
        var center = await _centers.GetByIdAsync(centerId, cancellationToken);
        return ApiResponse.Success("تم جلب تفاصيل المركز الصحي بنجاح.", center);
    }

    [HttpPost]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> Create([FromBody] CreateMedicalCenterDto dto, CancellationToken cancellationToken = default)
    {
        var created = await _centers.CreateAsync(dto, cancellationToken);
        return ApiResponse.Success("تم إنشاء المركز الصحي بنجاح.", created);
    }

    [HttpPut("{centerId:guid}")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> Update(
        Guid centerId,
        [FromBody] UpdateMedicalCenterDto dto,
        CancellationToken cancellationToken = default)
    {
        var updated = await _centers.UpdateAsync(centerId, dto, cancellationToken);
        return ApiResponse.Success("تم تحديث بيانات المركز الصحي بنجاح.", updated);
    }

    [HttpPost("{centerId:guid}/activate")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> Activate(Guid centerId, CancellationToken cancellationToken = default)
    {
        var center = await _centers.ActivateAsync(centerId, cancellationToken);
        return ApiResponse.Success("تم تفعيل المركز الصحي بنجاح.", center);
    }

    [HttpPost("{centerId:guid}/deactivate")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> Deactivate(
        Guid centerId,
        [FromBody] DeactivateMedicalCenterDto dto,
        CancellationToken cancellationToken = default)
    {
        var center = await _centers.DeactivateAsync(centerId, dto, cancellationToken);
        return ApiResponse.Success("تم تعطيل المركز الصحي بنجاح.", center);
    }

    [HttpDelete("{centerId:guid}")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public IResult Delete(Guid centerId)
    {
        _ = centerId;
        throw new MedicalCenterPhysicalDeleteNotAllowedException();
    }
}
