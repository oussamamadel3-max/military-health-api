using API.Infrastructure;
using Application.Common.Permissions;
using Application.Dtos.Centers;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// ترحيل السجلات التاريخية وإثبات التحقق قبل السماح بمركز نشط ثانٍ (FR-012). عملية مركزية بصلاحية إدارة المراكز.
/// </summary>
[ApiController]
[Authorize]
[Route("api/historical-migration")]
public class HistoricalCenterMigrationController : ControllerBase
{
    private readonly IHistoricalCenterMigrationService _migration;

    public HistoricalCenterMigrationController(IHistoricalCenterMigrationService migration)
        => _migration = migration;

    [HttpGet("preview")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> Preview(CancellationToken cancellationToken)
    {
        var report = await _migration.PreviewAsync(cancellationToken);
        return ApiResponse.Success("تم إعداد تقرير السجلات غير المرتبطة بمركز صالح.", report);
    }

    [HttpPost("execute")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> Execute(
        [FromBody] ExecuteHistoricalMigrationDto dto,
        CancellationToken cancellationToken)
    {
        var report = await _migration.ExecuteAsync(dto.TargetCenterId, cancellationToken);
        return ApiResponse.Success(
            "تم إسناد السجلات القابلة للإصلاح. النسخة الاحتياطية الكاملة شرط تشغيلي خارج النظام، والتحقق لم يُغلق بعد.",
            report);
    }

    [HttpPost("verify")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> Verify(
        [FromBody] ExecuteHistoricalMigrationDto? dto,
        CancellationToken cancellationToken)
    {
        var status = await _migration.VerifyAsync(dto?.TargetCenterId, cancellationToken);
        return ApiResponse.Success("تم تسجيل نتيجة التحقق من ترحيل السجلات التاريخية.", status);
    }

    [HttpGet("status")]
    [AuthorizePermission(MilitaryHealthPermissions.Centers.Manage)]
    public async Task<IResult> Status(CancellationToken cancellationToken)
    {
        var status = await _migration.GetStatusAsync(cancellationToken);
        return ApiResponse.Success("تم جلب حالة ترحيل السجلات التاريخية.", status);
    }
}
