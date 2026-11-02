using API.Infrastructure;
using API.RateLimiting;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Export;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

/// <summary>
/// تصدير ملفات المنتسبين للتجنيد وسجل الدفعات والتنزيل المحمي (FR-058…FR-061).
/// </summary>
[ApiController]
[Authorize]
[Route("api/export/recruitment")]
public class RecruitmentExportController : ControllerBase
{
    private readonly IRecruitmentExportService _exports;

    public RecruitmentExportController(IRecruitmentExportService exports)
    {
        _exports = exports;
    }

    /// <summary>
    /// يعيد الملفات ذات القرار النهائي (مقبول/مرفوض) غير المصدَّرة ضمن المركز النشط.
    /// </summary>
    [HttpGet("pending")]
    [AuthorizeOperation(MilitaryHealthPermissions.Export.Recruitment, OperationScope.Local)]
    public async Task<IResult> ListPending(
        [FromQuery] RecruitmentExportPendingQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await _exports.ListPendingAsync(
            query,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        var message = page.TotalCount == 0
            ? "لا توجد ملفات معلقة للتصدير."
            : "تم جلب الملفات الجاهزة للتصدير بنجاح.";

        return ApiResponse.Success(message, page);
    }

    /// <summary>
    /// ينشئ دفعة تصدير ويولّد ملف XLSX ويربطه بـ FileAsset، مع تعليم المنتسبين عند النجاح فقط.
    /// </summary>
    [HttpPost("batches")]
    [AuthorizeOperation(MilitaryHealthPermissions.Export.Recruitment, OperationScope.Local)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IResult> CreateBatch(
        [FromBody] CreateRecruitmentExportBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _exports.CreateBatchAsync(
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success("تم إنشاء دفعة التصدير وتوليد الملف بنجاح.", result, status: 201);
    }

    /// <summary>يعيد سجل دفعات التصدير ضمن المركز النشط.</summary>
    [HttpGet("batches")]
    [AuthorizeOperation(MilitaryHealthPermissions.Export.ViewHistory, OperationScope.Local)]
    public async Task<IResult> ListBatches(
        [FromQuery] RecruitmentExportBatchQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await _exports.ListBatchesAsync(
            query,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        var message = page.TotalCount == 0
            ? "لا توجد دفعات تصدير ضمن التصفية الحالية."
            : "تم جلب سجل دفعات التصدير بنجاح.";

        return ApiResponse.Success(message, page);
    }

    /// <summary>يعيد تفاصيل دفعة تصدير مع عناصرها اللقطية.</summary>
    [HttpGet("batches/{exportBatchId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Export.ViewHistory, OperationScope.Local)]
    public async Task<IResult> GetBatch(
        Guid exportBatchId,
        CancellationToken cancellationToken = default)
    {
        var detail = await _exports.GetBatchAsync(
            exportBatchId,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success("تم جلب تفاصيل دفعة التصدير بنجاح.", detail);
    }

    /// <summary>تنزيل محمي لملف دفعة تصدير مكتملة.</summary>
    [HttpGet("batches/{exportBatchId:guid}/download")]
    [AuthorizeOperation(MilitaryHealthPermissions.Export.Recruitment, OperationScope.Local)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> DownloadBatch(
        Guid exportBatchId,
        CancellationToken cancellationToken = default)
    {
        var file = await _exports.DownloadBatchFileAsync(
            exportBatchId,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>سجل تاريخي بالملفات المصدَّرة ضمن المركز النشط (FR-060).</summary>
    [HttpGet("history")]
    [AuthorizeOperation(MilitaryHealthPermissions.Export.ViewHistory, OperationScope.Local)]
    public async Task<IResult> ListHistory(
        [FromQuery] RecruitmentExportHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await _exports.ListHistoryAsync(
            query,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        var message = page.TotalCount == 0
            ? "لا توجد ملفات مصدَّرة ضمن التصفية الحالية."
            : "تم جلب سجل الملفات المصدَّرة بنجاح.";

        return ApiResponse.Success(message, page);
    }

    /// <summary>يصدّر سجل المصدَّر إلى Excel وفق صلاحية عرض السجل.</summary>
    [HttpGet("history/export")]
    [AuthorizeOperation(MilitaryHealthPermissions.Export.ViewHistory, OperationScope.Local)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> ExportHistory(
        [FromQuery] RecruitmentExportHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var file = await _exports.ExportHistoryExcelAsync(
            query,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return File(file.FileContents, file.ContentType, file.FileName);
    }
}
