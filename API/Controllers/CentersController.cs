using System.ComponentModel.DataAnnotations;
using API.Infrastructure;
using API.RateLimiting;
using Application.Common.Permissions;
using Application.Dtos.AuthDtos;
using Application.Dtos.Centers;
using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

/// <summary>
/// إدارة المراكز الصحية وربط العيادات (MH-P2-030). مسارات اختيار المركز النشط تبقى في ActiveCenterController تحت api/centers.
/// </summary>
[ApiController]
[Authorize]
[Route("api/centers")]
public class CentersController : ControllerBase
{
    private readonly IMedicalCenterService _centers;
    private readonly ICenterClinicService _centerClinics;
    private readonly IUserClinicAssignmentService _clinicAssignments;

    public CentersController(
        IMedicalCenterService centers,
        ICenterClinicService centerClinics,
        IUserClinicAssignmentService clinicAssignments)
    {
        _centers = centers;
        _centerClinics = centerClinics;
        _clinicAssignments = clinicAssignments;
    }

    [HttpGet]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    [EnableRateLimiting(RateLimitPolicies.ReferenceData)]
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

    [HttpGet("{centerId:guid}/clinics")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> GetCenterClinics(
        Guid centerId,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _centerClinics.GetByCenterAsync(centerId, pageNumber, pageSize, isActive, cancellationToken);
        return ApiResponse.Success("تم جلب عيادات المركز بنجاح.", result);
    }

    [HttpPost("{centerId:guid}/clinics")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> LinkClinic(
        Guid centerId,
        [FromBody] LinkCenterClinicDto dto,
        CancellationToken cancellationToken = default)
    {
        var linked = await _centerClinics.LinkAsync(centerId, dto, cancellationToken);
        return ApiResponse.Success("تم ربط العيادة بالمركز بنجاح.", linked);
    }

    [HttpPost("{centerId:guid}/clinics/{centerClinicId:guid}/activate")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> ActivateClinicLink(
        Guid centerId,
        Guid centerClinicId,
        CancellationToken cancellationToken = default)
    {
        var link = await _centerClinics.ActivateAsync(centerId, centerClinicId, cancellationToken);
        return ApiResponse.Success("تم تفعيل ربط العيادة بالمركز بنجاح.", link);
    }

    [HttpPost("{centerId:guid}/clinics/{centerClinicId:guid}/deactivate")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> DeactivateClinicLink(
        Guid centerId,
        Guid centerClinicId,
        CancellationToken cancellationToken = default)
    {
        var link = await _centerClinics.DeactivateAsync(centerId, centerClinicId, cancellationToken);
        return ApiResponse.Success("تم تعطيل ربط العيادة بالمركز بنجاح.", link);
    }

    [HttpDelete("{centerId:guid}/clinics/{centerClinicId:guid}")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public IResult DeleteClinicLink(Guid centerId, Guid centerClinicId)
    {
        _ = centerId;
        _ = centerClinicId;
        throw new CenterClinicPhysicalDeleteNotAllowedException();
    }

    /// <summary>عيادات المركز لإدارة إسناد الأطباء (FR-020) — متاحة لمدير المركز دون Centers.Manage.</summary>
    [HttpGet("{centerId:guid}/clinic-assignments/clinics")]
    [AuthorizePermission(MilitaryHealthPermissions.UserClinics.Manage)]
    public async Task<IResult> GetClinicsForDoctorAssignment(
        Guid centerId,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _clinicAssignments.GetCenterClinicsAsync(
            centerId, pageNumber, pageSize, isActive, cancellationToken);
        return ApiResponse.Success("تم جلب عيادات المركز لإسناد الأطباء بنجاح.", result);
    }

    [HttpGet("{centerId:guid}/clinic-assignments/doctors")]
    [AuthorizePermission(MilitaryHealthPermissions.UserClinics.Manage)]
    public async Task<IResult> GetCenterDoctorsForAssignment(
        Guid centerId,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _clinicAssignments.GetCenterDoctorsAsync(
            centerId, pageNumber, pageSize, cancellationToken);
        return ApiResponse.Success("تم جلب أطباء المركز بنجاح.", result);
    }

    [HttpGet("{centerId:guid}/clinics/{clinicId:guid}/doctors")]
    [AuthorizePermission(MilitaryHealthPermissions.UserClinics.Manage)]
    public async Task<IResult> GetClinicAssignedDoctors(
        Guid centerId,
        Guid clinicId,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = await _clinicAssignments.GetClinicDoctorsAsync(
            centerId, clinicId, pageNumber, pageSize, activeOnly, cancellationToken);
        return ApiResponse.Success("تم جلب الأطباء المسندين للعيادة بنجاح.", result);
    }
}
