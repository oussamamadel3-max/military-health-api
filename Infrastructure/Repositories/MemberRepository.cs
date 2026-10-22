using Application.Interfaces.Repositories;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>مستودع المنتسب باستخدام AppDbContext الحالي فقط.</summary>
public class MemberRepository : IMemberRepository
{
    private readonly AppDbContext _context;

    public MemberRepository(AppDbContext context) => _context = context;

    public IQueryable<Member> Query(bool asNoTracking = true)
    {
        var query = _context.Members.AsQueryable();
        return asNoTracking ? query.AsNoTracking() : query;
    }

    public async Task<Member?> GetByIdAsync(Guid memberId, bool asNoTracking = false, CancellationToken cancellationToken = default)
    {
        var query = IncludeLookups(_context.Members.AsQueryable());
        if (asNoTracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(m => m.MemberId == memberId, cancellationToken);
    }

    public async Task<Member?> GetByIdInCenterAsync(
        Guid memberId,
        Guid centerId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = IncludeLookups(_context.Members.AsQueryable());
        if (asNoTracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(
            m => m.MemberId == memberId && m.CurrentMedicalCenterId == centerId,
            cancellationToken);
    }

    public Task<bool> RecruitmentNumberExistsAsync(
        string recruitmentNumber,
        Guid? excludingMemberId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = recruitmentNumber.Trim();
        var query = _context.Members.AsNoTracking()
            .Where(m => m.RecruitmentNumber.ToLower() == normalized.ToLower());
        if (excludingMemberId.HasValue)
            query = query.Where(m => m.MemberId != excludingMemberId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public async Task<(long FileNumber, int QueueNumber)> AllocateFileAndQueueNumbersAsync(
        Guid centerId,
        DateOnly queueDate,
        CancellationToken cancellationToken = default)
    {
        var maxFile = await _context.Members.AsNoTracking()
            .MaxAsync(m => (long?)m.FileNumber, cancellationToken) ?? 0L;

        var maxQueue = await _context.Members.AsNoTracking()
            .Where(m => m.QueueMedicalCenterId == centerId && m.QueueDate == queueDate)
            .MaxAsync(m => (int?)m.QueueNumber, cancellationToken) ?? 0;

        return (maxFile + 1, maxQueue + 1);
    }

    public void Add(Member member) => _context.Members.Add(member);

    public void AddIdentity(MemberCenterIdentity identity) => _context.MemberCenterIdentities.Add(identity);

    private static IQueryable<Member> IncludeLookups(IQueryable<Member> query)
        => query
            .Include(m => m.MaritalStatus)
            .Include(m => m.RecruitmentCenter)
            .Include(m => m.BloodType)
            .Include(m => m.CurrentMedicalCenter);
}
