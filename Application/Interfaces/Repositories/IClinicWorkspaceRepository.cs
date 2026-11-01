using Domain.Entities.Clinical;

namespace Application.Interfaces.Repositories;

/// <summary>وصول قراءة/كتابة لحالة الفحص السريرية وأقفال التحرير في مساحة عمل العيادة.</summary>
public interface IClinicWorkspaceRepository
{
    IQueryable<MedicalExam> QueryMedicalExams(bool asNoTracking = true);

    IQueryable<ClinicAssessment> QueryClinicAssessments(bool asNoTracking = true);

    IQueryable<DeferredAction> QueryDeferredActions(bool asNoTracking = true);

    /// <summary>قراءة القرارات النهائية الموجودة (ملف المشرف / إصدار القرار).</summary>
    IQueryable<FinalDecision> QueryFinalDecisions(bool asNoTracking = true);

    IQueryable<ClinicExamEditLock> QueryEditLocks(bool asNoTracking = true);

    Task<ClinicExamEditLock?> GetEditLockAsync(
        Guid memberId,
        Guid clinicId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default);

    void AddEditLock(ClinicExamEditLock editLock);

    void RemoveEditLock(ClinicExamEditLock editLock);

    void AddMedicalExam(MedicalExam exam);

    void AddClinicAssessment(ClinicAssessment assessment);

    void AddDeferredAction(DeferredAction deferredAction);

    IQueryable<EyeExamDetail> QueryEyeExamDetails(bool asNoTracking = true);

    IQueryable<EyeRefractionDetail> QueryEyeRefractionDetails(bool asNoTracking = true);

    void AddEyeExamDetail(EyeExamDetail detail);

    void AddEyeRefractionDetail(EyeRefractionDetail detail);

    IQueryable<InternalExamDetail> QueryInternalExamDetails(bool asNoTracking = true);

    void AddInternalExamDetail(InternalExamDetail detail);

    IQueryable<SurgeryExamDetail> QuerySurgeryExamDetails(bool asNoTracking = true);

    void AddSurgeryExamDetail(SurgeryExamDetail detail);

    IQueryable<OrthopedicExamDetail> QueryOrthopedicExamDetails(bool asNoTracking = true);

    void AddOrthopedicExamDetail(OrthopedicExamDetail detail);

    IQueryable<EntExamDetail> QueryEntExamDetails(bool asNoTracking = true);

    void AddEntExamDetail(EntExamDetail detail);
}
