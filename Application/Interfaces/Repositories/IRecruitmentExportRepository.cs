using Domain.Entities.Core;
using Domain.Entities.Export;

namespace Application.Interfaces.Repositories;

/// <summary>
/// مستودع دفعات وعناصر تصدير التجنيد على جداول export الموجودة.
/// </summary>
public interface IRecruitmentExportRepository
{
    IQueryable<RecruitmentExportBatch> QueryBatches(bool asNoTracking = true);

    IQueryable<RecruitmentExportItem> QueryItems(bool asNoTracking = true);

    void AddBatch(RecruitmentExportBatch batch);

    void AddItem(RecruitmentExportItem item);

    void AddFileAsset(FileAsset fileAsset);

    Task<RecruitmentExportBatch?> GetBatchByIdInCenterAsync(
        Guid exportBatchId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// آخر دفعة مكتملة للمنتسب نفسه، لربط إعادة التصدير (FR-061).
    /// </summary>
    Task<Guid?> GetLastCompletedBatchIdForMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);
}
