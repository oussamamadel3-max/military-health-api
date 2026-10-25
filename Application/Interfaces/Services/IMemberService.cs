using Application.Common.Results;
using Application.Dtos.ExportDtos;
using Application.Dtos.Members;
using Microsoft.AspNetCore.Http;

namespace Application.Interfaces.Services;

/// <summary>خدمات ملف المنتسب: محلي (CRUD/إيصال/Excel) ومركزي (بحث/عرض مع تدقيق عابر).</summary>
public interface IMemberService
{
    Task<PaginatedList<MemberListItemDto>> ListAsync(
        MemberListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>قائمة/بحث عبر المراكز بصلاحية مركزية مع تسجيل الاطلاع العابر.</summary>
    Task<PaginatedList<MemberListItemDto>> ListCentralAsync(
        MemberListQuery query,
        string accessReason,
        Guid? targetCenterId = null,
        CancellationToken cancellationToken = default);

    Task<MemberDetailsDto> GetByIdAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>تفاصيل منتسب عبر المراكز بصلاحية مركزية مع تسجيل الاطلاع العابر.</summary>
    Task<MemberDetailsDto> GetByIdCentralAsync(
        Guid memberId,
        string accessReason,
        CancellationToken cancellationToken = default);

    Task<MemberDetailsDto> CreateAsync(
        CreateMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    Task<MemberDetailsDto> UpdateAsync(
        Guid memberId,
        UpdateMemberRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يولّد PDF إيصال المنتسب والباركود ضمن المركز النشط دون تعديل أرقام الملف أو الدور.</summary>
    Task<ExportFileDto> GetReceiptPdfAsync(
        Guid memberId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>ينزّل قالب استيراد Excel المعتمد.</summary>
    Task<ExportFileDto> GetImportTemplateAsync(
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يرفع ملف Excel ويعيد معاينة دون حفظ منتسبين.</summary>
    Task<MemberImportPreviewResult> PreviewImportAsync(
        IFormFile file,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يعتمد السجلات الصالحة من جلسة معاينة مرة واحدة فقط.</summary>
    Task<MemberImportCommitResult> CommitImportAsync(
        Guid importId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يلغي جلسة معاينة دون إنشاء منتسبين.</summary>
    Task CancelImportAsync(
        Guid importId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>ينزّل تقرير أخطاء جلسة الاستيراد.</summary>
    Task<ExportFileDto> GetImportErrorsAsync(
        Guid importId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>يصدّر قائمة المنتسبين وفق تصفية القائمة ونطاق المركز النشط.</summary>
    Task<ExportFileDto> ExportAsync(
        MemberListQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
