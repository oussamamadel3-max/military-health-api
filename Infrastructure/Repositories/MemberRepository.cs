using Application.Interfaces.Repositories;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>مستودع المنتسب باستخدام AppDbContext الحالي فقط.</summary>
public class MemberRepository : IMemberRepository
{
    /// <summary>مفتاح فئة قفل رقم الملف العالمي (pg_advisory_xact_lock key1).</summary>
    private const int FileNumberLockClass = 720_220;

    /// <summary>مفتاح فئة قفل رقم الدور اليومي لكل مركز (pg_advisory_xact_lock key1).</summary>
    private const int QueueNumberLockClass = 720_221;

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

    /// <summary>
    /// يخصص رقم ملف عالمي ورقم دور يومي للمركز ضمن المعاملة الحالية.
    /// على PostgreSQL يُؤخذ قفل معاملة (pg_advisory_xact_lock) قبل MAX+1 لمنع سباق التزامن؛ يُتخطى على InMemory.
    /// يراعي أيضاً صفوف <see cref="Member"/> المضافة/المعدَّلة في ChangeTracker ولم تُحفظ بعد،
    /// حتى لا تتصادم أرقام الدفعة الواحدة (مثل اعتماد استيراد متعدد الصفوف).
    /// </summary>
    public async Task<(long FileNumber, int QueueNumber)> AllocateFileAndQueueNumbersAsync(
        Guid centerId,
        DateOnly queueDate,
        CancellationToken cancellationToken = default)
    {
        await AcquireAllocationLocksAsync(centerId, queueDate, cancellationToken);

        var maxFileDb = await _context.Members.AsNoTracking()
            .MaxAsync(m => (long?)m.FileNumber, cancellationToken) ?? 0L;

        var maxQueueDb = await _context.Members.AsNoTracking()
            .Where(m => m.QueueMedicalCenterId == centerId && m.QueueDate == queueDate)
            .MaxAsync(m => (int?)m.QueueNumber, cancellationToken) ?? 0;

        var pending = _context.ChangeTracker.Entries<Member>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity)
            .ToList();

        var maxFilePending = pending.Count == 0 ? 0L : pending.Max(m => m.FileNumber);
        var maxQueuePending = pending
            .Where(m => m.QueueMedicalCenterId == centerId && m.QueueDate == queueDate)
            .Select(m => (int?)m.QueueNumber)
            .DefaultIfEmpty(null)
            .Max() ?? 0;

        return (Math.Max(maxFileDb, maxFilePending) + 1, Math.Max(maxQueueDb, maxQueuePending) + 1);
    }

    public void Add(Member member) => _context.Members.Add(member);

    public void AddIdentity(MemberCenterIdentity identity) => _context.MemberCenterIdentities.Add(identity);

    public async Task<IReadOnlyList<MemberGeneralNote>> GetActiveNotesAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        return await _context.MemberGeneralNotes.AsNoTracking()
            .Include(n => n.AuthorClinic)
            .Include(n => n.AuthorUser)
            .Where(n => n.MemberId == memberId && n.IsActive)
            .OrderByDescending(n => n.WrittenAt)
            .ToListAsync(cancellationToken);
    }

    public Task<MemberTransfer?> GetLastTransferAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        return _context.MemberTransfers.AsNoTracking()
            .Include(t => t.FromCenter)
            .Include(t => t.ToCenter)
            .Where(t => t.MemberId == memberId)
            .OrderByDescending(t => t.ExecutedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task AcquireAllocationLocksAsync(
        Guid centerId,
        DateOnly queueDate,
        CancellationToken cancellationToken)
    {
        if (!IsNpgsqlProvider())
            return;

        // قفل عالمي لتسلسل رقم الملف؛ يُحرَّر تلقائياً عند Commit/Rollback.
        await _context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1})",
            parameters: new object[] { FileNumberLockClass, 1 },
            cancellationToken: cancellationToken);

        // قفل مستقل لكل (مركز، يوم) لتسلسل رقم الدور.
        var queueKey2 = HashCode.Combine(centerId, queueDate.DayNumber);
        await _context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1})",
            parameters: new object[] { QueueNumberLockClass, queueKey2 },
            cancellationToken: cancellationToken);
    }

    private bool IsNpgsqlProvider()
        => _context.Database.ProviderName is { } name
           && name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    private static IQueryable<Member> IncludeLookups(IQueryable<Member> query)
        => query
            .Include(m => m.MaritalStatus)
            .Include(m => m.RecruitmentCenter)
            .Include(m => m.BloodType)
            .Include(m => m.CurrentMedicalCenter);
}
