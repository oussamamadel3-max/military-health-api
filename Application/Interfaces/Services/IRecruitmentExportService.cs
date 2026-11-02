using Application.Common.Results;
using Application.Dtos.Export;
using Application.Dtos.ExportDtos;

namespace Application.Interfaces.Services;

/// <summary>
/// دورة التصدير الرسمية لملفات المنتسبين إلى التجنيد (FR-058…FR-061).
/// </summary>
public interface IRecruitmentExportService
{
    /// <summary>قائمة الملفات ذات القرار النهائي غير المصدَّرة في المركز النشط.</summary>
    Task<PaginatedList<RecruitmentExportPendingItemDto>> ListPendingAsync(
        RecruitmentExportPendingQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ينشئ دفعة GENERATING ثم يولّد XLSX ويربط FileAsset ويعلّم المنتسبين عند النجاح فقط.
    /// </summary>
    Task<RecruitmentExportBatchResultDto> CreateBatchAsync(
        CreateRecruitmentExportBatchRequest request,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>سجل دفعات التصدير ضمن المركز النشط.</summary>
    Task<PaginatedList<RecruitmentExportBatchSummaryDto>> ListBatchesAsync(
        RecruitmentExportBatchQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>تفاصيل دفعة مع عناصرها.</summary>
    Task<RecruitmentExportBatchDetailDto> GetBatchAsync(
        Guid exportBatchId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>تنزيل محمي لملف دفعة مكتملة عبر FileAsset.</summary>
    Task<ExportFileDto> DownloadBatchFileAsync(
        Guid exportBatchId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>سجل تاريخي بعناصر الدفعات المكتملة (FR-060).</summary>
    Task<PaginatedList<RecruitmentExportHistoryItemDto>> ListHistoryAsync(
        RecruitmentExportHistoryQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>تصدير سجل المصدَّر إلى Excel وفق الصلاحية.</summary>
    Task<ExportFileDto> ExportHistoryExcelAsync(
        RecruitmentExportHistoryQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);
}
