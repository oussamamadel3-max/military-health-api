using Domain.Entities.Clinical;

namespace Application.Interfaces.Repositories;

/// <summary>وصول قراءة/كتابة لحالة الفحص السريرية وأقفال التحرير في مساحة عمل العيادة.</summary>
public interface IClinicWorkspaceRepository
{
    IQueryable<MedicalExam> QueryMedicalExams(bool asNoTracking = true);

    IQueryable<ClinicAssessment> QueryClinicAssessments(bool asNoTracking = true);

    IQueryable<DeferredAction> QueryDeferredActions(bool asNoTracking = true);

    IQueryable<ClinicExamEditLock> QueryEditLocks(bool asNoTracking = true);

    Task<ClinicExamEditLock?> GetEditLockAsync(
        Guid memberId,
        Guid clinicId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    void AddEditLock(ClinicExamEditLock editLock);

    void RemoveEditLock(ClinicExamEditLock editLock);
}
