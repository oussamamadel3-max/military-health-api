using Application.Interfaces.Repositories;
using Domain.Entities.Archive;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

/// <summary>مستودع أرشيف المنتسب باستخدام AppDbContext الحالي فقط.</summary>
public class MemberArchiveRepository : IMemberArchiveRepository
{
    /// <summary>مفتاح فئة قفل تسلسل الرقم الأرشيفي لكل مركز (pg_advisory_xact_lock key1).</summary>
    private const int ArchiveNumberLockClass = 720_230;

    private readonly AppDbContext _context;

    public MemberArchiveRepository(AppDbContext context) => _context = context;

    public IQueryable<MemberArchive> Query(bool asNoTracking = true)
    {
        var query = _context.MemberArchives.AsQueryable();
        return asNoTracking ? query.AsNoTracking() : query;
    }

    public Task<MemberArchive?> GetByFinalDecisionIdAsync(
        Guid finalDecisionId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.MemberArchives.AsQueryable();
        if (asNoTracking) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(a => a.FinalDecisionId == finalDecisionId, cancellationToken);
    }

    public Task<MemberArchive?> GetByMemberIdAsync(
        Guid memberId,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.MemberArchives.AsQueryable();
        if (asNoTracking) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(a => a.MemberId == memberId, cancellationToken);
    }

    /// <summary>
    /// يخصص الرقم التالي بصيغة {CenterCode}-{seq:D8} مع مراعاة الصفوف المعلّقة في ChangeTracker.
    /// على PostgreSQL يُؤخذ قفل معاملة قبل MAX+1 لمنع سباق التزامن (FR-069 / A1).
    /// </summary>
    public async Task<string> AllocateArchiveNumberAsync(
        Guid centerId,
        string centerCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(centerCode);

        await AcquireAllocationLockAsync(centerId, cancellationToken);

        var code = centerCode.Trim();
        var prefix = code + "-";

        var existingNumbers = await _context.MemberArchives.AsNoTracking()
            .Where(a => a.CenterId == centerId)
            .Select(a => a.ArchiveNumber)
            .ToListAsync(cancellationToken);

        var pendingNumbers = _context.ChangeTracker.Entries<MemberArchive>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified
                && e.Entity.CenterId == centerId)
            .Select(e => e.Entity.ArchiveNumber)
            .ToList();

        var maxSeq = 0L;
        foreach (var number in existingNumbers.Concat(pendingNumbers))
        {
            if (TryParseSequence(number, prefix, out var seq) && seq > maxSeq)
                maxSeq = seq;
        }

        return $"{prefix}{maxSeq + 1:D8}";
    }

    public void Add(MemberArchive archive) => _context.MemberArchives.Add(archive);

    private async Task AcquireAllocationLockAsync(Guid centerId, CancellationToken cancellationToken)
    {
        if (!IsNpgsqlProvider())
            return;

        var key2 = HashCode.Combine(centerId);
        await _context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1})",
            parameters: new object[] { ArchiveNumberLockClass, key2 },
            cancellationToken: cancellationToken);
    }

    private bool IsNpgsqlProvider()
        => _context.Database.ProviderName is { } name
           && name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseSequence(string archiveNumber, string prefix, out long sequence)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(archiveNumber))
            return false;

        if (!archiveNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = archiveNumber[prefix.Length..];
        return long.TryParse(suffix, out sequence) && sequence > 0;
    }
}
