using API.Infrastructure;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// عرض مجمّع لاستشارات واستقصاءات ومرفقات المنتسب ضمن مساحة عمل العيادة (FR-050).
/// </summary>
[ApiController]
[Authorize]
[Route("api/clinic-workspace")]
public class MemberMedicalRequestsController : ControllerBase
{
    private readonly IMemberMedicalRequestService _medicalRequests;

    public MemberMedicalRequestsController(IMemberMedicalRequestService medicalRequests)
    {
        _medicalRequests = medicalRequests;
    }

    /// <summary>
    /// يعيد الاستشارات والاستقصاءات المرتبطة بالمنتسب مصنّفة مع المرفقات المسموح بها.
    /// </summary>
    [HttpGet("members/{memberId:guid}/medical-requests")]
    [AuthorizeOperation(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, OperationScope.Local)]
    public async Task<IResult> GetForMember(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var result = await _medicalRequests.GetForMemberAsync(
            memberId,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        var message = result.IsEmpty
            ? result.EmptyMessageAr ?? "لا توجد استشارات أو استقصاءات."
            : "تم جلب الاستشارات والاستقصاءات بنجاح.";

        return ApiResponse.Success(message, result);
    }
}
