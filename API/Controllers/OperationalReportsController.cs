using API.Infrastructure;
using API.RateLimiting;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.OperationalReports;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

/// <summary>
/// مؤشرات وتقارير تشغيلية محلية ومركزية (FR-062…FR-068).
/// منفصلة عن ReportsController القالبي (تدقيق/لوحة نشاط).
/// </summary>
[ApiController]
[Authorize]
[Route("api/operational-reports")]
public class OperationalReportsController : ControllerBase
{
    private readonly IOperationalReportsService _reports;

    public OperationalReportsController(IOperationalReportsService reports)
    {
        _reports = reports;
    }

    /// <summary>لوحة مؤشرات المركز النشط (FR-062).</summary>
    [HttpGet("local/kpis")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Local, OperationScope.Local)]
    public async Task<IResult> LocalKpis(
        [FromQuery] LocalKpiQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await _reports.GetLocalKpisAsync(query, ClientCenterBinding.Read(Request), cancellationToken);
        var message = data.TotalMembers == 0
            ? "لا توجد بيانات ضمن الفترة المحددة."
            : "تم جلب مؤشرات المركز بنجاح.";
        return ApiResponse.Success(message, data);
    }

    /// <summary>مؤشرات مجمعة عبر المراكز (FR-063).</summary>
    [HttpGet("central/kpis")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Central, OperationScope.Central)]
    public async Task<IResult> CentralKpis(
        [FromQuery] CentralKpiQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await _reports.GetCentralKpisAsync(query, cancellationToken);
        return ApiResponse.Success("تم جلب المؤشرات المركزية بنجاح.", data);
    }

    /// <summary>تصدير المؤشرات المركزية إلى Excel (FR-063).</summary>
    [HttpGet("central/kpis/export")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Central, OperationScope.Central)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> ExportCentralKpis(
        [FromQuery] CentralKpiQuery query,
        CancellationToken cancellationToken = default)
    {
        var file = await _reports.ExportCentralKpisAsync(query, cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>القائمة النهائية للمنتسبين (FR-064).</summary>
    [HttpGet("local/final-members")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Local, OperationScope.Local)]
    public async Task<IResult> FinalMembers(
        [FromQuery] FinalMembersQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await _reports.ListFinalMembersAsync(query, ClientCenterBinding.Read(Request), cancellationToken);
        var message = page.TotalCount == 0
            ? "لا توجد نتائج ضمن التصفية الحالية."
            : "تم جلب القائمة النهائية بنجاح.";
        return ApiResponse.Success(message, page);
    }

    /// <summary>تصدير القائمة النهائية إلى Excel (FR-064).</summary>
    [HttpGet("local/final-members/export")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Local, OperationScope.Local)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> ExportFinalMembers(
        [FromQuery] FinalMembersQuery query,
        CancellationToken cancellationToken = default)
    {
        var file = await _reports.ExportFinalMembersAsync(query, ClientCenterBinding.Read(Request), cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>تقرير الحالات المؤجلة المحلي (FR-065).</summary>
    [HttpGet("local/deferred")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Local, OperationScope.Local)]
    public async Task<IResult> LocalDeferred(
        [FromQuery] DeferredReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await _reports.GetDeferredReportAsync(
            query, ClientCenterBinding.Read(Request), central: false, cancellationToken);
        var message = data.TotalCount == 0
            ? "لا توجد حالات مؤجلة ضمن التصفية الحالية."
            : "تم جلب تقرير المؤجلات بنجاح.";
        return ApiResponse.Success(message, data);
    }

    /// <summary>تصدير تقرير المؤجلات المحلي (FR-065).</summary>
    [HttpGet("local/deferred/export")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Local, OperationScope.Local)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> ExportLocalDeferred(
        [FromQuery] DeferredReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var file = await _reports.ExportDeferredReportAsync(
            query, ClientCenterBinding.Read(Request), central: false, cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>تقرير المؤجلات المركزي (FR-065 A4).</summary>
    [HttpGet("central/deferred")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Central, OperationScope.Central)]
    public async Task<IResult> CentralDeferred(
        [FromQuery] DeferredReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await _reports.GetDeferredReportAsync(query, central: true, cancellationToken: cancellationToken);
        return ApiResponse.Success("تم جلب تقرير المؤجلات المركزي بنجاح.", data);
    }

    /// <summary>تقرير محلي بالأعداد والنسب (FR-066).</summary>
    [HttpGet("local/summary")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Local, OperationScope.Local)]
    public async Task<IResult> LocalSummary(
        [FromQuery] LocalSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await _reports.GetLocalSummaryAsync(query, ClientCenterBinding.Read(Request), cancellationToken);
        return ApiResponse.Success("تم جلب التقرير المحلي بنجاح.", data);
    }

    /// <summary>تصدير التقرير المحلي (FR-066).</summary>
    [HttpGet("local/summary/export")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Local, OperationScope.Local)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> ExportLocalSummary(
        [FromQuery] LocalSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        var file = await _reports.ExportLocalSummaryAsync(query, ClientCenterBinding.Read(Request), cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>مقارنة المراكز (FR-067).</summary>
    [HttpGet("central/compare")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Central, OperationScope.Central)]
    public async Task<IResult> CentralCompare(
        [FromQuery] CentralCompareQuery query,
        CancellationToken cancellationToken = default)
    {
        var data = await _reports.GetCentralCompareAsync(query, cancellationToken);
        return ApiResponse.Success("تم جلب المقارنة المركزية بنجاح.", data);
    }

    /// <summary>تصدير المقارنة المركزية (FR-067).</summary>
    [HttpGet("central/compare/export")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Central, OperationScope.Central)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> ExportCentralCompare(
        [FromQuery] CentralCompareQuery query,
        CancellationToken cancellationToken = default)
    {
        var file = await _reports.ExportCentralCompareAsync(query, cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>ربط المنتسبين بالأطباء (FR-068).</summary>
    [HttpGet("local/doctor-linkage")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Local, OperationScope.Local)]
    public async Task<IResult> DoctorLinkage(
        [FromQuery] DoctorLinkageQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new DoctorLinkageQuery();
        if (string.Equals(query.ViewMode, DoctorLinkageViewModes.ByDoctor, StringComparison.OrdinalIgnoreCase))
        {
            var byDoctor = await _reports.ListDoctorLinkageByDoctorAsync(
                query, ClientCenterBinding.Read(Request), cancellationToken);
            var msg = byDoctor.TotalCount == 0
                ? "لا توجد بيانات ضمن التصفية الحالية."
                : "تم جلب تقرير الربط حسب الطبيب بنجاح.";
            return ApiResponse.Success(msg, byDoctor);
        }

        var byMember = await _reports.ListDoctorLinkageByMemberAsync(
            query, ClientCenterBinding.Read(Request), cancellationToken);
        var message = byMember.TotalCount == 0
            ? "لا توجد بيانات ضمن التصفية الحالية."
            : "تم جلب تقرير الربط حسب المنتسب بنجاح.";
        return ApiResponse.Success(message, byMember);
    }

    /// <summary>تصدير ربط الأطباء (FR-068).</summary>
    [HttpGet("local/doctor-linkage/export")]
    [AuthorizeOperation(MilitaryHealthPermissions.Reports.Local, OperationScope.Local)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> ExportDoctorLinkage(
        [FromQuery] DoctorLinkageQuery query,
        CancellationToken cancellationToken = default)
    {
        var file = await _reports.ExportDoctorLinkageAsync(
            query, ClientCenterBinding.Read(Request), cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }
}
