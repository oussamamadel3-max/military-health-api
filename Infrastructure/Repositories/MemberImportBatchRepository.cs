using Application.Interfaces.Repositories;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>مستودع جلسات استيراد المنتسبين.</summary>
public class MemberImportBatchRepository : IMemberImportBatchRepository
{
    private readonly AppDbContext _context;

    public MemberImportBatchRepository(AppDbContext context) => _context = context;

    public void Add(MemberImportBatch batch) => _context.MemberImportBatches.Add(batch);

    public Task<MemberImportBatch?> GetByIdInCenterAsync(
        Guid importBatchId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var q = _context.MemberImportBatches.AsQueryable();
        if (asNoTracking)
            q = q.AsNoTracking();

        return q.FirstOrDefaultAsync(
            b => b.ImportBatchId == importBatchId && b.CenterId == centerId,
            cancellationToken);
    }
}
