using Domain.Entities.Patient;

namespace Application.Interfaces.Repositories;

/// <summary>مستودع جلسات استيراد المنتسبين من Excel.</summary>
public interface IMemberImportBatchRepository
{
    void Add(MemberImportBatch batch);

    Task<MemberImportBatch?> GetByIdInCenterAsync(
        Guid importBatchId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);
}
