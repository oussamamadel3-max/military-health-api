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
}
