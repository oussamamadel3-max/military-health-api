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
/// نقل ملف المنتسب بين المراكز وعرض تاريخ النقل (FR-073 / FR-074).
/// </summary>
[ApiController]
[Authorize]
[Route("api/members")]
public class MemberTransferController : ControllerBase
{
    private readonly IMemberTransferService _transfers;

    public MemberTransferController(IMemberTransferService transfers)
    {
        _transfers = transfers;
    }

    /// <summary>
    /// ينقل ملف المنتسب من المركز النشط إلى مركز هدف فعّال مع سبب إلزامي.
    /// </summary>
    [HttpPost("{memberId:guid}/transfer")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.Transfer, OperationScope.Local)]
    public async Task<IResult> TransferLocal(
        Guid memberId,
        [FromBody] TransferMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _transfers.TransferLocalAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success("تم نقل ملف المنتسب بنجاح.", result);
    }

    /// <summary>
    /// ينقل ملف المنتسب مركزياً بصلاحية النقل دون اشتراط مركز نشط محلي.
    /// </summary>
    [HttpPost("central/{memberId:guid}/transfer")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.Transfer, OperationScope.Central)]
    public async Task<IResult> TransferCentral(
        Guid memberId,
        [FromBody] TransferMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _transfers.TransferCentralAsync(memberId, request, cancellationToken);
        return ApiResponse.Success("تم نقل ملف المنتسب بنجاح.", result);
    }

    /// <summary>يعيد تاريخ نقل الملف ضمن المركز النشط (صلاحية العرض المحلي).</summary>
    [HttpGet("{memberId:guid}/transfers")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> GetHistoryLocal(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var history = await _transfers.GetHistoryLocalAsync(
            memberId,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        var message = history.HasTransfers
            ? "تم جلب تاريخ نقل الملف بنجاح."
            : history.EmptyMessageAr ?? "لم يُنقل هذا الملف بين المراكز.";

        return ApiResponse.Success(message, history);
    }

    /// <summary>يعيد تاريخ نقل الملف مركزياً مع سبب اطلاع عابر.</summary>
    [HttpGet("central/{memberId:guid}/transfers")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.ViewCentral, OperationScope.Central)]
    public async Task<IResult> GetHistoryCentral(
        Guid memberId,
        [FromQuery][Required] string accessReason,
        CancellationToken cancellationToken = default)
    {
        var history = await _transfers.GetHistoryCentralAsync(memberId, accessReason, cancellationToken);

        var message = history.HasTransfers
            ? "تم جلب تاريخ نقل الملف المركزي بنجاح."
            : history.EmptyMessageAr ?? "لم يُنقل هذا الملف بين المراكز.";

        return ApiResponse.Success(message, history);
    }
}
