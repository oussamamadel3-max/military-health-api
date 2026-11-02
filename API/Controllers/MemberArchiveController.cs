using API.Infrastructure;
using API.RateLimiting;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Archive;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

/// <summary>
/// بحث الأرشيف والتقرير الطبي الكامل والطباعة (FR-070…FR-072).
/// يعتمد على سجل الأرشيف المُنشأ بعد القرار النهائي دون إنشاء أرشيف موازٍ.
/// </summary>
[ApiController]
[Authorize]
[Route("api/archive")]
public class MemberArchiveController : ControllerBase
{
    private readonly IMemberArchiveService _archives;

    public MemberArchiveController(IMemberArchiveService archives)
    {
        _archives = archives;
    }

    /// <summary>بحث الملفات المؤرشفة ضمن المركز النشط (FR-070).</summary>
    [HttpGet("search")]
    [AuthorizeOperation(MilitaryHealthPermissions.Archive.ViewPrint, OperationScope.Local)]
    public async Task<IResult> SearchLocal(
        [FromQuery] ArchiveSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await _archives.SearchLocalAsync(
            query,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        var message = page.TotalCount == 0
            ? "لا توجد نتائج ضمن التصفية الحالية."
            : "تم جلب نتائج الأرشيف بنجاح.";

        return ApiResponse.Success(message, page);
    }

    /// <summary>بحث مركزي في الأرشيف مع تسجيل الاطلاع العابر (FR-070 A4).</summary>
    [HttpGet("central/search")]
    [AuthorizeOperation(MilitaryHealthPermissions.Archive.ViewPrint, OperationScope.Central)]
    public async Task<IResult> SearchCentral(
        [FromQuery] ArchiveSearchQuery query,
        [FromQuery] string accessReason,
        CancellationToken cancellationToken = default)
    {
        var page = await _archives.SearchCentralAsync(query, accessReason, cancellationToken);
        var message = page.TotalCount == 0
            ? "لا توجد نتائج ضمن التصفية الحالية."
            : "تم جلب نتائج الأرشيف المركزي بنجاح.";
        return ApiResponse.Success(message, page);
    }

    /// <summary>التقرير الطبي الكامل لسجل أرشيف ضمن المركز النشط (FR-071).</summary>
    [HttpGet("{archiveId:guid}/report")]
    [AuthorizeOperation(MilitaryHealthPermissions.Archive.ViewPrint, OperationScope.Local)]
    public async Task<IResult> GetReportLocal(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        var report = await _archives.GetFullReportLocalAsync(
            archiveId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب التقرير الطبي الكامل بنجاح.", report);
    }

    /// <summary>التقرير الطبي الكامل بمسار مركزي (FR-071).</summary>
    [HttpGet("central/{archiveId:guid}/report")]
    [AuthorizeOperation(MilitaryHealthPermissions.Archive.ViewPrint, OperationScope.Central)]
    public async Task<IResult> GetReportCentral(
        Guid archiveId,
        [FromQuery] string accessReason,
        CancellationToken cancellationToken = default)
    {
        var report = await _archives.GetFullReportCentralAsync(
            archiveId,
            accessReason,
            cancellationToken);
        return ApiResponse.Success("تم جلب التقرير الطبي الكامل بنجاح.", report);
    }

    /// <summary>طباعة PDF عربي RTL بمقاس A4 (FR-072).</summary>
    [HttpGet("{archiveId:guid}/print")]
    [AuthorizeOperation(MilitaryHealthPermissions.Archive.ViewPrint, OperationScope.Local)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> PrintLocal(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        var file = await _archives.PrintFullReportLocalAsync(
            archiveId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>طباعة PDF بمسار مركزي (FR-072).</summary>
    [HttpGet("central/{archiveId:guid}/print")]
    [AuthorizeOperation(MilitaryHealthPermissions.Archive.ViewPrint, OperationScope.Central)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> PrintCentral(
        Guid archiveId,
        [FromQuery] string accessReason,
        CancellationToken cancellationToken = default)
    {
        var file = await _archives.PrintFullReportCentralAsync(
            archiveId,
            accessReason,
            cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }
}
