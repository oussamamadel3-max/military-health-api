using System.ComponentModel.DataAnnotations;
using API.Infrastructure;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Members;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// مساحة عمل الطبيب: عيادات المسند، بحث المنتسب، وفتح ملخص الملف ضمن العيادة (FR-032 / FR-045 فتح).
/// </summary>
[ApiController]
[Authorize]
[Route("api/clinic-workspace")]
public class ClinicWorkspaceController : ControllerBase
{
    private readonly IClinicWorkspaceService _workspace;

    public ClinicWorkspaceController(IClinicWorkspaceService workspace) => _workspace = workspace;

    /// <summary>عيادات الطبيب النشطة ضمن المركز النشط.</summary>
    [HttpGet("my-clinics")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> ListMyClinics(CancellationToken cancellationToken = default)
    {
        var clinics = await _workspace.ListMyClinicsAsync(
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب عيادات الطبيب بنجاح.", clinics);
    }

    /// <summary>بحث المنتسبين ضمن العيادة المسندة والمركز النشط دون اكتساب قفل تحرير.</summary>
    [HttpGet("members")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> SearchMembers(
        [FromQuery][Required] Guid clinicId,
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery] int? queueNumber = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _workspace.SearchMembersAsync(
            clinicId,
            new MemberListQuery
            {
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                QueueNumber = queueNumber,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success("تم جلب نتائج بحث العيادة بنجاح.", result);
    }

    /// <summary>فتح ملخص ملف المنتسب في سياق العيادة مع قفل التحرير وتدقيق الاطلاع.</summary>
    [HttpGet("members/{memberId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> OpenMember(
        Guid memberId,
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var summary = await _workspace.OpenMemberAsync(
            memberId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم فتح ملخص ملف المنتسب في العيادة بنجاح.", summary);
    }
}
