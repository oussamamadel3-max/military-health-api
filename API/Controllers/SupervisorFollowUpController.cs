using API.Infrastructure;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Supervisor;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// قائمة متابعة المشرف المحلية مع البحث والتصفية (FR-057).
/// </summary>
[ApiController]
[Authorize]
[Route("api/supervisor")]
public class SupervisorFollowUpController : ControllerBase
{
    private readonly ISupervisorFollowUpService _followUp;

    public SupervisorFollowUpController(ISupervisorFollowUpService followUp)
    {
        _followUp = followUp;
    }

    /// <summary>
    /// يعيد قائمة منتسبي المركز النشط مع حالات الاكتمال والقرار والمؤجلات،
    /// مع البحث والتصفية والترقيم دون تفاصيل سريرية غير لازمة.
    /// </summary>
    [HttpGet("members")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.FinalDecision, OperationScope.Local)]
    public async Task<IResult> List(
        [FromQuery] SupervisorFollowUpQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = await _followUp.ListAsync(
            query,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        var message = page.TotalCount == 0
            ? "لا توجد نتائج ضمن التصفية الحالية."
            : "تم جلب قائمة متابعة المشرف بنجاح.";

        return ApiResponse.Success(message, page);
    }
}
