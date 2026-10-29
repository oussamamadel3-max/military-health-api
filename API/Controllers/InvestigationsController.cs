using System.ComponentModel.DataAnnotations;
using API.Infrastructure;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.ClinicWorkspace;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// استقصاءات مخبرية وشعاعية ضمن مساحة عمل الطبيب: إنشاء، نتيجة، عرض، قائمة ومتابعة المعلّق (FR-047).
/// </summary>
[ApiController]
[Authorize]
[Route("api/clinic-workspace")]
public class InvestigationsController : ControllerBase
{
    private readonly IInvestigationService _investigations;

    public InvestigationsController(IInvestigationService investigations)
    {
        _investigations = investigations;
    }

    /// <summary>ينشئ استقصاء مخبرياً أو شعاعياً مرتبطاً بالمنتسب وعيادة الطبيب والمركز النشط.</summary>
    [HttpPost("members/{memberId:guid}/investigations")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> Create(
        Guid memberId,
        [FromBody] CreateInvestigationRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await _investigations.CreateAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم إنشاء الاستقصاء بنجاح.", created);
    }

    /// <summary>يسجّل نتيجة استقصاء معلّق ويحدّث الحالة والتاريخ دون إغلاق الإجراء المؤجّل.</summary>
    [HttpPut("investigations/{investigationId:guid}/result")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> UpdateResult(
        Guid investigationId,
        [FromBody] UpdateInvestigationResultRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = await _investigations.UpdateResultAsync(
            investigationId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم تسجيل نتيجة الاستقصاء بنجاح.", updated);
    }

    /// <summary>يعيد استقصاء بالمعرّف ضمن المركز والعيادة المسندة.</summary>
    [HttpGet("investigations/{investigationId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> GetById(
        Guid investigationId,
        CancellationToken cancellationToken = default)
    {
        var item = await _investigations.GetByIdAsync(
            investigationId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب الاستقصاء بنجاح.", item);
    }

    /// <summary>يعيد استقصاءات المنتسب مع تصفية اختيارية بالعيادة والحالة.</summary>
    [HttpGet("members/{memberId:guid}/investigations")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> ListByMember(
        Guid memberId,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var items = await _investigations.ListByMemberAsync(
            memberId,
            clinicId,
            status,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب استقصاءات المنتسب بنجاح.", items);
    }

    /// <summary>يعيد الاستقصاءات المعلّقة في العيادة المسندة لمتابعة الطلبات.</summary>
    [HttpGet("investigations/pending")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> ListPending(
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var items = await _investigations.ListPendingAsync(
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب الاستقصاءات المعلّقة بنجاح.", items);
    }
}
