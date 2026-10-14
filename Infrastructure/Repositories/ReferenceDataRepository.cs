using Application.Interfaces.Repositories;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class ReferenceDataRepository : IReferenceDataRepository
{
    private readonly AppDbContext _context;

    public ReferenceDataRepository(AppDbContext context) => _context = context;

    public IQueryable<RecruitmentCenter> RecruitmentCenters(bool asNoTracking = true)
        => Track(_context.RecruitmentCenters, asNoTracking);

    public IQueryable<Clinic> Clinics(bool asNoTracking = true)
        => Track(_context.Clinics, asNoTracking);

    public IQueryable<ResultStatus> ResultStatuses(bool asNoTracking = true)
        => Track(_context.ResultStatuses, asNoTracking);

    public IQueryable<RequiredActionType> RequiredActionTypes(bool asNoTracking = true)
        => Track(_context.RequiredActionTypes, asNoTracking);

    public IQueryable<MaritalStatus> MaritalStatuses(bool asNoTracking = true)
        => Track(_context.MaritalStatuses, asNoTracking);

    public IQueryable<BloodType> BloodTypes(bool asNoTracking = true)
        => Track(_context.BloodTypes, asNoTracking);

    public IQueryable<ConsultationType> ConsultationTypes(bool asNoTracking = true)
        => Track(_context.ConsultationTypes, asNoTracking);

    public IQueryable<InvestigationType> InvestigationTypes(bool asNoTracking = true)
        => Track(_context.InvestigationTypes, asNoTracking);

    public IQueryable<BmiCategory> BmiCategories(bool asNoTracking = true)
        => Track(_context.BmiCategories, asNoTracking);

    public IQueryable<ValidationRule> ValidationRules(bool asNoTracking = true)
        => Track(_context.ValidationRules, asNoTracking);

    public void Add(RecruitmentCenter entity) => _context.RecruitmentCenters.Add(entity);
    public void Add(Clinic entity) => _context.Clinics.Add(entity);
    public void Add(RequiredActionType entity) => _context.RequiredActionTypes.Add(entity);
    public void Add(MaritalStatus entity) => _context.MaritalStatuses.Add(entity);
    public void Add(BloodType entity) => _context.BloodTypes.Add(entity);
    public void Add(ConsultationType entity) => _context.ConsultationTypes.Add(entity);
    public void Add(InvestigationType entity) => _context.InvestigationTypes.Add(entity);
    public void Add(BmiCategory entity) => _context.BmiCategories.Add(entity);
    public void Add(ValidationRule entity) => _context.ValidationRules.Add(entity);

    private static IQueryable<T> Track<T>(IQueryable<T> query, bool asNoTracking) where T : class
        => asNoTracking ? query.AsNoTracking() : query;
}
