using Domain.Entities.Medical;

namespace Application.Interfaces.Repositories;

/// <summary>وصول قراءة/كتابة لسجلات الاستشارات التخصصية ضمن النطاق الطبي.</summary>
public interface IConsultationRepository
{
    /// <summary>استعلام الاستشارات؛ يفعّل AsNoTracking عند الطلب.</summary>
    IQueryable<Consultation> Query(bool asNoTracking = true);

    /// <summary>يجلب استشارة ضمن مركز محدد.</summary>
    Task<Consultation?> GetByIdInCenterAsync(
        Guid consultationId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    /// <summary>يضيف استشارة جديدة للتتبع.</summary>
    void Add(Consultation consultation);
}
