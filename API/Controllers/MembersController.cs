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
/// مسارات ملف المنتسب في المركز النشط (أساس MH-P3-010: إنشاء/تعديل/قائمة/تفاصيل).
/// </summary>
[ApiController]
[Authorize]
[Route("api/members")]
public class MembersController : ControllerBase
{
    private readonly IMemberService _members;

    public MembersController(IMemberService members) => _members = members;

    /// <summary>قائمة وبحث المنتسبين ضمن المركز النشط.</summary>
    [HttpGet]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> List(
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery] DateOnly? date = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _members.ListAsync(
            new MemberListQuery
            {
                FileNumber = fileNumber,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                Date = date,
                FromDate = fromDate,
                ToDate = toDate,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success("تم جلب قائمة المنتسبين بنجاح.", result);
    }

    /// <summary>تفاصيل ملف منتسب ضمن المركز النشط.</summary>
    [HttpGet("{memberId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> GetById(Guid memberId, CancellationToken cancellationToken = default)
    {
        var details = await _members.GetByIdAsync(
            memberId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب تفاصيل ملف المنتسب بنجاح.", details);
    }

    /// <summary>إنشاء ملف منتسب في المركز النشط.</summary>
    [HttpPost]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.CreateUpdate, OperationScope.Local)]
    public async Task<IResult> Create([FromBody] CreateMemberRequest request, CancellationToken cancellationToken = default)
    {
        var created = await _members.CreateAsync(
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم إنشاء ملف المنتسب بنجاح.", created);
    }

    /// <summary>تعديل بيانات منتسب ضمن المركز النشط.</summary>
    [HttpPut("{memberId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.CreateUpdate, OperationScope.Local)]
    public async Task<IResult> Update(
        Guid memberId,
        [FromBody] UpdateMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = await _members.UpdateAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم تحديث ملف المنتسب بنجاح.", updated);
    }
}
