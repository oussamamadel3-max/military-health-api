using API.Infrastructure;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.ClinicWorkspace;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// استشارات تخصصية ضمن مساحة عمل الطبيب: إنشاء، تسجيل النتيجة، عرض وقائمة (FR-046).
/// </summary>
[ApiController]
[Authorize]
[Route("api/clinic-workspace")]
public class ConsultationsController : ControllerBase
{
    private readonly IConsultationService _consultations;

    public ConsultationsController(IConsultationService consultations)
    {
        _consultations = consultations;
    }

    /// <summary>ينشئ استشارة تخصصية مرتبطة بالمنتسب وعيادة الطبيب والمركز النشط.</summary>
    [HttpPost("members/{memberId:guid}/consultations")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> Create(
        Guid memberId,
        [FromBody] CreateConsultationRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await _consultations.CreateAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم إنشاء الاستشارة بنجاح.", created);
    }

    /// <summary>يسجّل نتيجة استشارة معلّقة ويحدّث الحالة والتاريخ.</summary>
    [HttpPut("consultations/{consultationId:guid}/result")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> UpdateResult(
        Guid consultationId,
        [FromBody] UpdateConsultationResultRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = await _consultations.UpdateResultAsync(
            consultationId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم تسجيل نتيجة الاستشارة بنجاح.", updated);
    }

    /// <summary>يعيد استشارة بالمعرّف ضمن المركز والعيادة المسندة.</summary>
    [HttpGet("consultations/{consultationId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> GetById(
        Guid consultationId,
        CancellationToken cancellationToken = default)
    {
        var item = await _consultations.GetByIdAsync(
            consultationId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب الاستشارة بنجاح.", item);
    }

    /// <summary>يعيد استشارات المنتسب في المركز النشط مع تصفية اختيارية بالعيادة.</summary>
    [HttpGet("members/{memberId:guid}/consultations")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> ListByMember(
        Guid memberId,
        [FromQuery] Guid? clinicId = null,
        CancellationToken cancellationToken = default)
    {
        var items = await _consultations.ListByMemberAsync(
            memberId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب استشارات المنتسب بنجاح.", items);
    }
}
