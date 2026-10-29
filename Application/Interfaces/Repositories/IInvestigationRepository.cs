using Domain.Entities.Medical;

namespace Application.Interfaces.Repositories;

/// <summary>وصول قراءة/كتابة لسجلات الاستقصاءات المخبرية والشعاعية.</summary>
public interface IInvestigationRepository
{
    /// <summary>استعلام الاستقصاءات؛ يفعّل AsNoTracking عند الطلب.</summary>
    IQueryable<Investigation> Query(bool asNoTracking = true);

    /// <summary>يجلب استقصاء ضمن مركز محدد.</summary>
    Task<Investigation?> GetByIdInCenterAsync(
        Guid investigationId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    /// <summary>يضيف استقصاء جديداً للتتبع.</summary>
    void Add(Investigation investigation);
}
