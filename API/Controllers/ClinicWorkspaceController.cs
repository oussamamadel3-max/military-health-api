using System.ComponentModel.DataAnnotations;
using API.Infrastructure;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// مساحة عمل الطبيب: عيادات المسند، بحث/فتح الملف، حفظ التقييم، فحص العيون، وتحرير قفل الفحص
/// (FR-032 / FR-033 / FR-034 / FR-039 / FR-040 / FR-041 / FR-045).
/// </summary>
[ApiController]
[Authorize]
[Route("api/clinic-workspace")]
public class ClinicWorkspaceController : ControllerBase
{
    private readonly IClinicWorkspaceService _workspace;
    private readonly IClinicAssessmentService _assessments;
    private readonly IEyeExamService _eyeExams;

    public ClinicWorkspaceController(
        IClinicWorkspaceService workspace,
        IClinicAssessmentService assessments,
        IEyeExamService eyeExams)
    {
        _workspace = workspace;
        _assessments = assessments;
        _eyeExams = eyeExams;
    }

    /// <summary>عيادات الطبيب النشطة ضمن المركز النشط.</summary>
    [HttpGet("my-clinics")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> ListMyClinics(CancellationToken cancellationToken = default)
    {
        var clinics = await _workspace.ListMyClinicsAsync(
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب عيادات الطبيب بنجاح.", clinics);
    }

    /// <summary>بحث المنتسبين ضمن العيادة المسندة والمركز النشط دون اكتساب قفل تحرير.</summary>
    [HttpGet("members")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> SearchMembers(
        [FromQuery][Required] Guid clinicId,
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery] int? queueNumber = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _workspace.SearchMembersAsync(
            clinicId,
            new MemberListQuery
            {
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                QueueNumber = queueNumber,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success("تم جلب نتائج بحث العيادة بنجاح.", result);
    }

    /// <summary>فتح ملخص ملف المنتسب في سياق العيادة مع قفل التحرير وتدقيق الاطلاع.</summary>
    [HttpGet("members/{memberId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> OpenMember(
        Guid memberId,
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var summary = await _workspace.OpenMemberAsync(
            memberId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم فتح ملخص ملف المنتسب في العيادة بنجاح.", summary);
    }

    /// <summary>يحفظ هيكل الفحص الأساسي وتقييم العيادة (مع DeferredAction عند مؤجل) ويحرّر القفل.</summary>
    [HttpPost("members/{memberId:guid}/assessment")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.AssessmentWrite, OperationScope.Local)]
    public async Task<IResult> SaveAssessment(
        Guid memberId,
        [FromBody] SaveClinicAssessmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var saved = await _assessments.SaveAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم حفظ تقييم العيادة بنجاح.", saved);
    }

    /// <summary>يعرض فحص العيون الحالي والانكسارات كما أُدخلت (FR-033 / FR-039).</summary>
    [HttpGet("members/{memberId:guid}/eye-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> GetEyeExam(
        Guid memberId,
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var exam = await _eyeExams.GetAsync(
            memberId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب فحص العيون بنجاح.", exam);
    }

    /// <summary>
    /// يحفظ تفاصيل فحص العيون والانكسار وتقييم العيادة في عملية واحدة ويحرّر القفل
    /// (FR-033 / FR-034 / شريحة تعديل FR-039).
    /// </summary>
    [HttpPost("members/{memberId:guid}/eye-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.AssessmentWrite, OperationScope.Local)]
    public async Task<IResult> SaveEyeExam(
        Guid memberId,
        [FromBody] SaveEyeExamRequest request,
        CancellationToken cancellationToken = default)
    {
        var saved = await _eyeExams.SaveAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم حفظ فحص العيون بنجاح.", saved);
    }

    /// <summary>يحرّر قفل التحرير المملوك للطبيب الحالي دون حفظ تقييم.</summary>
    [HttpDelete("members/{memberId:guid}/edit-lock")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.AssessmentWrite, OperationScope.Local)]
    public async Task<IResult> ReleaseEditLock(
        Guid memberId,
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        await _assessments.ReleaseEditLockAsync(
            memberId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم تحرير قفل الفحص بنجاح.");
    }
}
