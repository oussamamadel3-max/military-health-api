using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Domain.Entities.Export;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>مستودع دفعات وعناصر تصدير التجنيد.</summary>
public class RecruitmentExportRepository : IRecruitmentExportRepository
{
    public const string StatusCompleted = "COMPLETED";

    private readonly AppDbContext _context;

    public RecruitmentExportRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public IQueryable<RecruitmentExportBatch> QueryBatches(bool asNoTracking = true)
    {
        var q = _context.RecruitmentExportBatches.AsQueryable();
        return asNoTracking ? q.AsNoTracking() : q;
    }

    /// <inheritdoc />
    public IQueryable<RecruitmentExportItem> QueryItems(bool asNoTracking = true)
    {
        var q = _context.RecruitmentExportItems.AsQueryable();
        return asNoTracking ? q.AsNoTracking() : q;
    }

    /// <inheritdoc />
    public void AddBatch(RecruitmentExportBatch batch) => _context.RecruitmentExportBatches.Add(batch);

    /// <inheritdoc />
    public void AddItem(RecruitmentExportItem item) => _context.RecruitmentExportItems.Add(item);

    /// <inheritdoc />
    public void AddFileAsset(FileAsset fileAsset) => _context.FileAssets.Add(fileAsset);

    /// <inheritdoc />
    public Task<RecruitmentExportBatch?> GetBatchByIdInCenterAsync(
        Guid exportBatchId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var q = _context.RecruitmentExportBatches.AsQueryable();
        if (asNoTracking)
            q = q.AsNoTracking();

        return q.FirstOrDefaultAsync(
            b => b.ExportBatchId == exportBatchId && b.CenterId == centerId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Guid?> GetLastCompletedBatchIdForMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        return (
            from item in _context.RecruitmentExportItems.AsNoTracking()
            join batch in _context.RecruitmentExportBatches.AsNoTracking()
                on item.ExportBatchId equals batch.ExportBatchId
            where item.MemberId == memberId && batch.Status == StatusCompleted
            orderby item.ExportedAt descending
            select (Guid?)item.ExportBatchId
        ).FirstOrDefaultAsync(cancellationToken);
    }
}
