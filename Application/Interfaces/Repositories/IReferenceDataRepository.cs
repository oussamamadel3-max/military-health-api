using Domain.Entities.Core;

namespace Application.Interfaces.Repositories;

public interface IReferenceDataRepository
{
    IQueryable<RecruitmentCenter> RecruitmentCenters(bool asNoTracking = true);
    IQueryable<Clinic> Clinics(bool asNoTracking = true);
    IQueryable<ResultStatus> ResultStatuses(bool asNoTracking = true);
    IQueryable<RequiredActionType> RequiredActionTypes(bool asNoTracking = true);
    IQueryable<MaritalStatus> MaritalStatuses(bool asNoTracking = true);
    IQueryable<BloodType> BloodTypes(bool asNoTracking = true);
    IQueryable<ConsultationType> ConsultationTypes(bool asNoTracking = true);
    IQueryable<InvestigationType> InvestigationTypes(bool asNoTracking = true);
    IQueryable<BmiCategory> BmiCategories(bool asNoTracking = true);
    IQueryable<ValidationRule> ValidationRules(bool asNoTracking = true);

    void Add(RecruitmentCenter entity);
    void Add(Clinic entity);
    void Add(RequiredActionType entity);
    void Add(MaritalStatus entity);
    void Add(BloodType entity);
    void Add(ConsultationType entity);
    void Add(InvestigationType entity);
    void Add(BmiCategory entity);
    void Add(ValidationRule entity);
}
