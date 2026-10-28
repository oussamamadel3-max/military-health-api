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
/// مساحة عمل الطبيب: عيادات المسند، بحث/فتح الملف، قائمة فحوصات العيون، الملاحظات العامة،
/// متابعة المؤجلات، حفظ التقييم، فحوصات العيادات، وتحرير قفل الفحص
/// (FR-032…FR-045).
/// </summary>
[ApiController]
[Authorize]
[Route("api/clinic-workspace")]
public class ClinicWorkspaceController : ControllerBase
{
    private readonly IClinicWorkspaceService _workspace;
    private readonly IClinicAssessmentService _assessments;
    private readonly IEyeExamService _eyeExams;
    private readonly IInternalExamService _internalExams;
    private readonly ISurgeryExamService _surgeryExams;
    private readonly IOrthopedicExamService _orthopedicExams;
    private readonly IEntExamService _entExams;

    public ClinicWorkspaceController(
        IClinicWorkspaceService workspace,
        IClinicAssessmentService assessments,
        IEyeExamService eyeExams,
        IInternalExamService internalExams,
        ISurgeryExamService surgeryExams,
        IOrthopedicExamService orthopedicExams,
        IEntExamService entExams)
    {
        _workspace = workspace;
        _assessments = assessments;
        _eyeExams = eyeExams;
        _internalExams = internalExams;
        _surgeryExams = surgeryExams;
        _orthopedicExams = orthopedicExams;
        _entExams = entExams;
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

    /// <summary>يُدخل أو يحدّث أو يُفرغ الملاحظة العامة للمنتسب (FR-043 / تنبيه FR-044).</summary>
    [HttpPut("members/{memberId:guid}/general-notes")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.GeneralNotes, OperationScope.Local)]
    public async Task<IResult> UpsertGeneralNote(
        Guid memberId,
        [FromBody] UpsertMemberGeneralNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var saved = await _workspace.UpsertGeneralNoteAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success(
            saved.HasGeneralNotesAlert
                ? "تم حفظ الملاحظة العامة بنجاح."
                : "تم إفراغ الملاحظة العامة وإيقاف التنبيه.",
            saved);
    }

    /// <summary>قائمة الحالات المؤجلة في العيادة ضمن المركز النشط مع التصفية والترقيم (FR-042).</summary>
    [HttpGet("deferred-actions")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.DeferredAction, OperationScope.Local)]
    public async Task<IResult> ListDeferredActions(
        [FromQuery][Required] Guid clinicId,
        [FromQuery] string? status = null,
        [FromQuery] short? actionTypeId = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _workspace.ListDeferredActionsAsync(
            new DeferredActionListQuery
            {
                ClinicId = clinicId,
                Status = status,
                ActionTypeId = actionTypeId,
                FromDate = fromDate,
                ToDate = toDate,
                FileNumber = fileNumber,
                FullName = fullName,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success(
            result.TotalCount == 0 ? "لا توجد نتائج." : "تم جلب قائمة الحالات المؤجلة بنجاح.",
            result);
    }

    /// <summary>يعلّم الإجراء المطلوب مكتملاً دون تغيير تقييم العيادة (FR-042).</summary>
    [HttpPost("deferred-actions/{deferredActionId:guid}/complete")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.DeferredAction, OperationScope.Local)]
    public async Task<IResult> CompleteDeferredAction(
        Guid deferredActionId,
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var completed = await _workspace.CompleteDeferredActionAsync(
            deferredActionId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم تعليم الإجراء مكتملاً بنجاح.", completed);
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

    /// <summary>قائمة فحوصات العيون الحالية في العيادة مع البحث والترقيم (شريحة قائمة FR-039).</summary>
    [HttpGet("eye-exams")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> ListEyeExams(
        [FromQuery][Required] Guid clinicId,
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _eyeExams.ListAsync(
            new EyeExamListQuery
            {
                ClinicId = clinicId,
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success(
            result.TotalCount == 0 ? "لا توجد نتائج." : "تم جلب قائمة فحوصات العيون بنجاح.",
            result);
    }

    /// <summary>قائمة فحوصات الباطنة الحالية مع البحث والترقيم (شريحة قائمة FR-039).</summary>
    [HttpGet("internal-exams")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> ListInternalExams(
        [FromQuery][Required] Guid clinicId,
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _internalExams.ListAsync(
            new ClinicExamListQuery
            {
                ClinicId = clinicId,
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success(
            result.TotalCount == 0 ? "لا توجد نتائج." : "تم جلب قائمة فحوصات الباطنة بنجاح.",
            result);
    }

    /// <summary>قائمة فحوصات الجراحة الحالية مع البحث والترقيم (شريحة قائمة FR-039).</summary>
    [HttpGet("surgery-exams")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> ListSurgeryExams(
        [FromQuery][Required] Guid clinicId,
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _surgeryExams.ListAsync(
            new ClinicExamListQuery
            {
                ClinicId = clinicId,
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success(
            result.TotalCount == 0 ? "لا توجد نتائج." : "تم جلب قائمة فحوصات الجراحة بنجاح.",
            result);
    }

    /// <summary>قائمة فحوصات العظمية الحالية مع البحث والترقيم (شريحة قائمة FR-039).</summary>
    [HttpGet("orthopedic-exams")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> ListOrthopedicExams(
        [FromQuery][Required] Guid clinicId,
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _orthopedicExams.ListAsync(
            new ClinicExamListQuery
            {
                ClinicId = clinicId,
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success(
            result.TotalCount == 0 ? "لا توجد نتائج." : "تم جلب قائمة فحوصات العظمية بنجاح.",
            result);
    }

    /// <summary>قائمة فحوصات الأذنية الحالية مع البحث والترقيم (شريحة قائمة FR-039).</summary>
    [HttpGet("ent-exams")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> ListEntExams(
        [FromQuery][Required] Guid clinicId,
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _entExams.ListAsync(
            new ClinicExamListQuery
            {
                ClinicId = clinicId,
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success(
            result.TotalCount == 0 ? "لا توجد نتائج." : "تم جلب قائمة فحوصات الأذنية بنجاح.",
            result);
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

    /// <summary>يعرض فحص الباطنة الحالي كما أُدخل (FR-035 / FR-039).</summary>
    [HttpGet("members/{memberId:guid}/internal-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> GetInternalExam(
        Guid memberId,
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var exam = await _internalExams.GetAsync(
            memberId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب فحص الباطنة بنجاح.", exam);
    }

    /// <summary>
    /// يحفظ تفاصيل فحص الباطنة وتقييم العيادة في عملية واحدة ويحرّر القفل
    /// (FR-035 / شريحة تعديل FR-039).
    /// </summary>
    [HttpPost("members/{memberId:guid}/internal-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.AssessmentWrite, OperationScope.Local)]
    public async Task<IResult> SaveInternalExam(
        Guid memberId,
        [FromBody] SaveInternalExamRequest request,
        CancellationToken cancellationToken = default)
    {
        var saved = await _internalExams.SaveAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم حفظ فحص الباطنة بنجاح.", saved);
    }

    /// <summary>يعرض فحص الجراحة الحالي كما أُدخل (FR-036 / FR-039).</summary>
    [HttpGet("members/{memberId:guid}/surgery-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> GetSurgeryExam(
        Guid memberId,
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var exam = await _surgeryExams.GetAsync(
            memberId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب فحص الجراحة بنجاح.", exam);
    }

    /// <summary>
    /// يحفظ تفاصيل فحص الجراحة وتقييم العيادة في عملية واحدة ويحرّر القفل
    /// (FR-036 / شريحة تعديل FR-039).
    /// </summary>
    [HttpPost("members/{memberId:guid}/surgery-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.AssessmentWrite, OperationScope.Local)]
    public async Task<IResult> SaveSurgeryExam(
        Guid memberId,
        [FromBody] SaveSurgeryExamRequest request,
        CancellationToken cancellationToken = default)
    {
        var saved = await _surgeryExams.SaveAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم حفظ فحص الجراحة بنجاح.", saved);
    }

    /// <summary>يعرض فحص العظمية الحالي كما أُدخل (FR-037 / FR-039).</summary>
    [HttpGet("members/{memberId:guid}/orthopedic-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> GetOrthopedicExam(
        Guid memberId,
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var exam = await _orthopedicExams.GetAsync(
            memberId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب فحص العظمية بنجاح.", exam);
    }

    /// <summary>
    /// يحفظ تفاصيل فحص العظمية وتقييم العيادة في عملية واحدة ويحرّر القفل
    /// (FR-037 / شريحة تعديل FR-039).
    /// </summary>
    [HttpPost("members/{memberId:guid}/orthopedic-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.AssessmentWrite, OperationScope.Local)]
    public async Task<IResult> SaveOrthopedicExam(
        Guid memberId,
        [FromBody] SaveOrthopedicExamRequest request,
        CancellationToken cancellationToken = default)
    {
        var saved = await _orthopedicExams.SaveAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم حفظ فحص العظمية بنجاح.", saved);
    }

    /// <summary>يعرض فحص الأذنية الحالي كما أُدخل (FR-038 / FR-039).</summary>
    [HttpGet("members/{memberId:guid}/ent-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> GetEntExam(
        Guid memberId,
        [FromQuery][Required] Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var exam = await _entExams.GetAsync(
            memberId,
            clinicId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب فحص الأذنية بنجاح.", exam);
    }

    /// <summary>
    /// يحفظ تفاصيل فحص الأذنية وتقييم العيادة في عملية واحدة ويحرّر القفل
    /// (FR-038 / شريحة تعديل FR-039).
    /// </summary>
    [HttpPost("members/{memberId:guid}/ent-exam")]
    [AuthorizeOperation(MilitaryHealthPermissions.Clinical.AssessmentWrite, OperationScope.Local)]
    public async Task<IResult> SaveEntExam(
        Guid memberId,
        [FromBody] SaveEntExamRequest request,
        CancellationToken cancellationToken = default)
    {
        var saved = await _entExams.SaveAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم حفظ فحص الأذنية بنجاح.", saved);
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
