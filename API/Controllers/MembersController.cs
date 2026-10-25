using System.ComponentModel.DataAnnotations;
using API.Infrastructure;
using API.RateLimiting;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Members;
using Application.Interfaces.Services;
using Application.Services.Members;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

/// <summary>
/// مسارات ملف المنتسب: محلي (CRUD، إيصال، Excel) ومركزي (بحث/عرض مع تدقيق عابر).
/// </summary>
[ApiController]
[Authorize]
[Route("api/members")]
public class MembersController : ControllerBase
{
    private readonly IMemberService _members;

    public MembersController(IMemberService members) => _members = members;

    /// <summary>قائمة وبحث المنتسبين ضمن المركز النشط.</summary>
    [HttpGet]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> List(
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery] DateOnly? date = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _members.ListAsync(
            new MemberListQuery
            {
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                Date = date,
                FromDate = fromDate,
                ToDate = toDate,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);

        return ApiResponse.Success("تم جلب قائمة المنتسبين بنجاح.", result);
    }

    /// <summary>قائمة وبحث المنتسبين عبر المراكز بصلاحية مركزية مع تسجيل الاطلاع العابر.</summary>
    [HttpGet("central")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.ViewCentral, OperationScope.Central)]
    public async Task<IResult> ListCentral(
        [FromQuery][Required] string accessReason,
        [FromQuery] Guid? centerId = null,
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery] DateOnly? date = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1,
        [FromQuery][Range(1, 500)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _members.ListCentralAsync(
            new MemberListQuery
            {
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                Date = date,
                FromDate = fromDate,
                ToDate = toDate,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            accessReason,
            centerId,
            cancellationToken);

        return ApiResponse.Success("تم جلب قائمة المنتسبين المركزية بنجاح.", result);
    }

    /// <summary>تفاصيل منتسب عبر المراكز بصلاحية مركزية مع تسجيل الاطلاع العابر.</summary>
    [HttpGet("central/{memberId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.ViewCentral, OperationScope.Central)]
    public async Task<IResult> GetByIdCentral(
        Guid memberId,
        [FromQuery][Required] string accessReason,
        CancellationToken cancellationToken = default)
    {
        var details = await _members.GetByIdCentralAsync(memberId, accessReason, cancellationToken);
        return ApiResponse.Success("تم جلب تفاصيل ملف المنتسب المركزية بنجاح.", details);
    }

    /// <summary>تصدير قائمة المنتسبين إلى Excel وفق التصفية ونطاق المركز النشط.</summary>
    [HttpGet("export")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.Export, OperationScope.Local)]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> Export(
        [FromQuery] long? fileNumber = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? recruitmentNumber = null,
        [FromQuery] string? fullName = null,
        [FromQuery] DateOnly? date = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var file = await _members.ExportAsync(
            new MemberListQuery
            {
                FileNumber = fileNumber,
                Barcode = barcode,
                RecruitmentNumber = recruitmentNumber,
                FullName = fullName,
                Date = date,
                FromDate = fromDate,
                ToDate = toDate
            },
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>تنزيل قالب استيراد المنتسبين المعتمد.</summary>
    [HttpGet("import/template")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.Import, OperationScope.Local)]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken cancellationToken = default)
    {
        var file = await _members.GetImportTemplateAsync(
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>رفع ملف Excel ومعاينة النتائج دون حفظ منتسبين.</summary>
    [HttpPost("import/preview")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.Import, OperationScope.Local)]
    [RequestSizeLimit(MemberImportExcel.MaxFileBytes + (512 * 1024))]
    public async Task<IResult> PreviewImport(IFormFile file, CancellationToken cancellationToken = default)
    {
        var preview = await _members.PreviewImportAsync(
            file,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تمت معاينة ملف الاستيراد بنجاح.", preview);
    }

    /// <summary>اعتماد السجلات الصالحة من جلسة المعاينة.</summary>
    [HttpPost("import/{importId:guid}/commit")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.Import, OperationScope.Local)]
    public async Task<IResult> CommitImport(Guid importId, CancellationToken cancellationToken = default)
    {
        var result = await _members.CommitImportAsync(
            importId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم اعتماد استيراد المنتسبين بنجاح.", result);
    }

    /// <summary>إلغاء جلسة معاينة الاستيراد دون حفظ.</summary>
    [HttpPost("import/{importId:guid}/cancel")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.Import, OperationScope.Local)]
    public async Task<IResult> CancelImport(Guid importId, CancellationToken cancellationToken = default)
    {
        await _members.CancelImportAsync(
            importId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم إلغاء جلسة الاستيراد.");
    }

    /// <summary>تنزيل تقرير أخطاء جلسة الاستيراد.</summary>
    [HttpGet("import/{importId:guid}/errors")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.Import, OperationScope.Local)]
    public async Task<IActionResult> DownloadImportErrors(Guid importId, CancellationToken cancellationToken = default)
    {
        var file = await _members.GetImportErrorsAsync(
            importId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return File(file.FileContents, file.ContentType, file.FileName);
    }

    /// <summary>تفاصيل ملف منتسب ضمن المركز النشط.</summary>
    [HttpGet("{memberId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.View, OperationScope.Local)]
    public async Task<IResult> GetById(Guid memberId, CancellationToken cancellationToken = default)
    {
        var details = await _members.GetByIdAsync(
            memberId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم جلب تفاصيل ملف المنتسب بنجاح.", details);
    }

    /// <summary>إنشاء ملف منتسب في المركز النشط.</summary>
    [HttpPost]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.CreateUpdate, OperationScope.Local)]
    public async Task<IResult> Create([FromBody] CreateMemberRequest request, CancellationToken cancellationToken = default)
    {
        var created = await _members.CreateAsync(
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم إنشاء ملف المنتسب بنجاح.", created);
    }

    /// <summary>تعديل بيانات منتسب ضمن المركز النشط.</summary>
    [HttpPut("{memberId:guid}")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.CreateUpdate, OperationScope.Local)]
    public async Task<IResult> Update(
        Guid memberId,
        [FromBody] UpdateMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        var updated = await _members.UpdateAsync(
            memberId,
            request,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return ApiResponse.Success("تم تحديث ملف المنتسب بنجاح.", updated);
    }

    /// <summary>معاينة/تحميل إيصال المنتسب والباركود (PDF) ضمن المركز النشط.</summary>
    [HttpGet("{memberId:guid}/receipt")]
    [AuthorizeOperation(MilitaryHealthPermissions.Members.PrintReceipt, OperationScope.Local)]
    public async Task<IActionResult> GetReceipt(Guid memberId, CancellationToken cancellationToken = default)
    {
        var pdf = await _members.GetReceiptPdfAsync(
            memberId,
            ClientCenterBinding.Read(Request),
            cancellationToken);
        return File(pdf.FileContents, pdf.ContentType, pdf.FileName);
    }
}
