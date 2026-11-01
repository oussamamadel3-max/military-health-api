using Application.Interfaces.Repositories;
using Domain.Entities.Clinical;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>استعلامات سريرية وأقفال تحرير لمساحة عمل العيادة عبر EF.</summary>
public class ClinicWorkspaceRepository : IClinicWorkspaceRepository
{
    private readonly AppDbContext _context;

    public ClinicWorkspaceRepository(AppDbContext context) => _context = context;

    public IQueryable<MedicalExam> QueryMedicalExams(bool asNoTracking = true)
        => asNoTracking ? _context.MedicalExams.AsNoTracking() : _context.MedicalExams;

    public IQueryable<ClinicAssessment> QueryClinicAssessments(bool asNoTracking = true)
        => asNoTracking ? _context.ClinicAssessments.AsNoTracking() : _context.ClinicAssessments;

    public IQueryable<DeferredAction> QueryDeferredActions(bool asNoTracking = true)
        => asNoTracking
            ? _context.DeferredActions.AsNoTracking().Include(x => x.ActionType)
            : _context.DeferredActions.Include(x => x.ActionType);

    public IQueryable<FinalDecision> QueryFinalDecisions(bool asNoTracking = true)
        => asNoTracking ? _context.FinalDecisions.AsNoTracking() : _context.FinalDecisions;

    public void AddFinalDecision(FinalDecision decision) => _context.FinalDecisions.Add(decision);

    public void AddSupervisorObjection(SupervisorObjection objection)
        => _context.SupervisorObjections.Add(objection);

    public IQueryable<ClinicExamEditLock> QueryEditLocks(bool asNoTracking = true)
        => asNoTracking
            ? _context.ClinicExamEditLocks.AsNoTracking().Include(x => x.LockedByUser)
            : _context.ClinicExamEditLocks.Include(x => x.LockedByUser);

    public async Task<ClinicExamEditLock?> GetEditLockAsync(
        Guid memberId,
        Guid clinicId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = asNoTracking
            ? _context.ClinicExamEditLocks.AsNoTracking().Include(x => x.LockedByUser)
            : _context.ClinicExamEditLocks.Include(x => x.LockedByUser);

        return await query.FirstOrDefaultAsync(
            x => x.MemberId == memberId && x.ClinicId == clinicId,
            cancellationToken);
    }

    public void AddEditLock(ClinicExamEditLock editLock) => _context.ClinicExamEditLocks.Add(editLock);

    public void RemoveEditLock(ClinicExamEditLock editLock) => _context.ClinicExamEditLocks.Remove(editLock);

    public void AddMedicalExam(MedicalExam exam) => _context.MedicalExams.Add(exam);

    public void AddClinicAssessment(ClinicAssessment assessment) => _context.ClinicAssessments.Add(assessment);

    public void AddDeferredAction(DeferredAction deferredAction) => _context.DeferredActions.Add(deferredAction);

    public IQueryable<EyeExamDetail> QueryEyeExamDetails(bool asNoTracking = true)
        => asNoTracking ? _context.EyeExamDetails.AsNoTracking() : _context.EyeExamDetails;

    public IQueryable<EyeRefractionDetail> QueryEyeRefractionDetails(bool asNoTracking = true)
        => asNoTracking ? _context.EyeRefractionDetails.AsNoTracking() : _context.EyeRefractionDetails;

    public void AddEyeExamDetail(EyeExamDetail detail) => _context.EyeExamDetails.Add(detail);

    public void AddEyeRefractionDetail(EyeRefractionDetail detail) => _context.EyeRefractionDetails.Add(detail);

    public IQueryable<InternalExamDetail> QueryInternalExamDetails(bool asNoTracking = true)
        => asNoTracking ? _context.InternalExamDetails.AsNoTracking() : _context.InternalExamDetails;

    public void AddInternalExamDetail(InternalExamDetail detail) => _context.InternalExamDetails.Add(detail);

    public IQueryable<SurgeryExamDetail> QuerySurgeryExamDetails(bool asNoTracking = true)
        => asNoTracking ? _context.SurgeryExamDetails.AsNoTracking() : _context.SurgeryExamDetails;

    public void AddSurgeryExamDetail(SurgeryExamDetail detail) => _context.SurgeryExamDetails.Add(detail);

    public IQueryable<OrthopedicExamDetail> QueryOrthopedicExamDetails(bool asNoTracking = true)
        => asNoTracking ? _context.OrthopedicExamDetails.AsNoTracking() : _context.OrthopedicExamDetails;

    public void AddOrthopedicExamDetail(OrthopedicExamDetail detail) => _context.OrthopedicExamDetails.Add(detail);

    public IQueryable<EntExamDetail> QueryEntExamDetails(bool asNoTracking = true)
        => asNoTracking ? _context.EntExamDetails.AsNoTracking() : _context.EntExamDetails;

    public void AddEntExamDetail(EntExamDetail detail) => _context.EntExamDetails.Add(detail);
}
