using API.Infrastructure;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Supervisor;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// إصدار القرار النهائي المعتمد ضمن مساحة عمل المشرف (FR-053 / FR-054 / FR-055).
/// التعديل مسار لاحق (MH-P6-040).
/// </summary>
[ApiController]
[Authorize]
[Route("api/supervisor")]
public class SupervisorFinalDecisionController : ControllerBase
{
    private readonly IFinalDecisionService _finalDecisions;

    public SupervisorFinalDecisionController(IFinalDecisionService finalDecisions)
    {
        _finalDecisions = finalDecisions;
    }

    /// <summary>
    /// يصدر قراراً نهائياً (قبول/رفض/مؤجل) للمنتسب بعد اكتمال العيادات الإلزامية،
    /// مع مبرر الاختلاف عند التعارض، وربط إجراء مؤجل بحالة مطلوب عند التأجيل.
    /// </summary>
    [HttpPost("members/{memberId:guid}/final-decision")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.FinalDecision, OperationScope.Local)]
    public async Task<IResult> Issue(
        Guid memberId,
        [FromBody] IssueFinalDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await _finalDecisions.IssueAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success("تم إصدار القرار النهائي المعتمد بنجاح.", created);
    }
}
