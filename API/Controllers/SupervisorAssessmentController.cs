using API.Infrastructure;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// مساحة عمل المشرف المحلية: ملف التقييم الطبي المجمّع تمهيداً للقرار النهائي (FR-051 / FR-052).
/// منفصلة عن clinic-workspace لتجنب دلالات قفل التحرير وإسناد العيادة.
/// </summary>
[ApiController]
[Authorize]
[Route("api/supervisor")]
public class SupervisorAssessmentController : ControllerBase
{
    private readonly ISupervisorAssessmentFileService _assessmentFiles;

    public SupervisorAssessmentController(ISupervisorAssessmentFileService assessmentFiles)
    {
        _assessmentFiles = assessmentFiles;
    }

    /// <summary>
    /// يعيد ملف التقييم المجمّع للمنتسب ضمن المركز النشط: بيانات المنتسب، العيادات الخمس،
    /// المؤجلات، والاستشارات/الاستقصاءات/المرفقات بعقد المرحلة الخامسة.
    /// </summary>
    [HttpGet("members/{memberId:guid}/assessment-file")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.FinalDecision, OperationScope.Local)]
    public async Task<IResult> GetAssessmentFile(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var result = await _assessmentFiles.GetAssessmentFileAsync(
            memberId,
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success("تم جلب ملف التقييم الطبي المجمّع بنجاح.", result);
    }
}
