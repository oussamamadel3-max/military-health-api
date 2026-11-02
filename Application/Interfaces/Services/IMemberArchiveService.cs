using Application.Common.Results;
using Application.Dtos.Archive;
using Application.Dtos.ExportDtos;

namespace Application.Interfaces.Services;

/// <summary>
/// بحث الأرشيف والتقرير الطبي الكامل والطباعة (FR-070…FR-072).
/// يعتمد على سجل الأرشيف المُنشأ عند القرار النهائي (FR-069) دون إنشاء أرشيف ثانٍ.
/// </summary>
public interface IMemberArchiveService
{
    /// <summary>بحث محلي في أرشيف المركز النشط.</summary>
    Task<PaginatedList<ArchiveSearchItemDto>> SearchLocalAsync(
        ArchiveSearchQuery query,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>بحث مركزي عبر المراكز مع تسجيل الاطلاع العابر.</summary>
    Task<PaginatedList<ArchiveSearchItemDto>> SearchCentralAsync(
        ArchiveSearchQuery query,
        string accessReason,
        CancellationToken cancellationToken = default);

    /// <summary>التقرير الكامل ضمن نطاق المركز النشط.</summary>
    Task<MemberFullMedicalReportDto> GetFullReportLocalAsync(
        Guid archiveId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>التقرير الكامل بمسار مركزي مع تسجيل الاطلاع العابر.</summary>
    Task<MemberFullMedicalReportDto> GetFullReportCentralAsync(
        Guid archiveId,
        string accessReason,
        CancellationToken cancellationToken = default);

    /// <summary>PDF عربي RTL بمقاس A4 للتقرير الكامل (محلي).</summary>
    Task<ExportFileDto> PrintFullReportLocalAsync(
        Guid archiveId,
        Guid? clientCenterId = null,
        CancellationToken cancellationToken = default);

    /// <summary>PDF عربي RTL بمقاس A4 للتقرير الكامل (مركزي).</summary>
    Task<ExportFileDto> PrintFullReportCentralAsync(
        Guid archiveId,
        string accessReason,
        CancellationToken cancellationToken = default);
}
