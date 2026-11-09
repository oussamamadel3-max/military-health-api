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

    /// <inheritdoc />
    public async Task LockMemberForUpdateAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        if (!IsNpgsqlProvider())
            return;

        // قفل صف داخل المعاملة الحالية؛ يُحرَّر تلقائياً عند Commit/Rollback.
        await _context.Database.ExecuteSqlRawAsync(
            """SELECT 1 FROM patient.member WHERE "memberId" = {0} FOR UPDATE""",
            parameters: new object[] { memberId },
            cancellationToken: cancellationToken);
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

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FindExistingRecruitmentNumbersAsync(
        IReadOnlyCollection<string> recruitmentNumbers,
        CancellationToken cancellationToken = default)
    {
        if (recruitmentNumbers.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var normalized = recruitmentNumbers
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var found = await _context.Members.AsNoTracking()
            .Where(m => normalized.Contains(m.RecruitmentNumber.ToLower()))
            .Select(m => m.RecruitmentNumber)
            .ToListAsync(cancellationToken);

        return found.ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    /// <inheritdoc />
    public async Task<int> AllocateDailyQueueNumberAsync(
        Guid centerId,
        DateOnly queueDate,
        CancellationToken cancellationToken = default)
    {
        await AcquireQueueAllocationLockAsync(centerId, queueDate, cancellationToken);

        var maxQueueDb = await _context.Members.AsNoTracking()
            .Where(m => m.QueueMedicalCenterId == centerId && m.QueueDate == queueDate)
            .MaxAsync(m => (int?)m.QueueNumber, cancellationToken) ?? 0;

        var maxQueuePending = _context.ChangeTracker.Entries<Member>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity)
            .Where(m => m.QueueMedicalCenterId == centerId && m.QueueDate == queueDate)
            .Select(m => (int?)m.QueueNumber)
            .DefaultIfEmpty(null)
            .Max() ?? 0;

        return Math.Max(maxQueueDb, maxQueuePending) + 1;
    }

    /// <inheritdoc />
    public Task<MemberCenterIdentity?> GetOpenIdentityForUpdateAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        return _context.MemberCenterIdentities
            .FirstOrDefaultAsync(
                i => i.MemberId == memberId && i.ValidTo == null,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Guid> ExecuteAuthorizedTransferAsync(
        Guid memberId,
        Guid toCenterId,
        string newCenterMemberId,
        string executedByUserId,
        string transferReason,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken = default)
    {
        if (!IsNpgsqlProvider())
            return await ExecuteTransferInMemoryAsync(
                memberId,
                toCenterId,
                newCenterMemberId,
                executedByUserId,
                transferReason,
                executedAt,
                cancellationToken);

        // EF cannot UPDATE currentMedicalCenterId directly (DB trigger); must use transferMember.
        var rows = await _context.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT patient."transferMember"({0}, {1}, {2}::citext, {3}, {4}, {5}) AS "Value"
                """,
                memberId,
                toCenterId,
                newCenterMemberId,
                executedByUserId,
                transferReason,
                executedAt)
            .ToListAsync(cancellationToken);

        var transferId = rows.SingleOrDefault();
        if (transferId == Guid.Empty)
            throw new InvalidOperationException("patient.transferMember returned no transfer id.");

        _context.ChangeTracker.Clear();
        return transferId;
    }

    /// <inheritdoc />
    public IQueryable<MemberTransfer> QueryTransfers(bool asNoTracking = true)
    {
        var query = _context.MemberTransfers
            .Include(t => t.FromCenter)
            .Include(t => t.ToCenter)
            .Include(t => t.ExecutedByUser)
            .AsQueryable();
        return asNoTracking ? query.AsNoTracking() : query;
    }

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

    public Task<MemberGeneralNote?> GetGeneralNoteForUpdateAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        return _context.MemberGeneralNotes
            .FirstOrDefaultAsync(n => n.MemberId == memberId, cancellationToken);
    }

    public void AddGeneralNote(MemberGeneralNote note) => _context.MemberGeneralNotes.Add(note);

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

        await AcquireQueueAllocationLockAsync(centerId, queueDate, cancellationToken);
    }

    private async Task AcquireQueueAllocationLockAsync(
        Guid centerId,
        DateOnly queueDate,
        CancellationToken cancellationToken)
    {
        if (!IsNpgsqlProvider())
            return;

        // قفل مستقل لكل (مركز، يوم) لتسلسل رقم الدور.
        var queueKey2 = HashCode.Combine(centerId, queueDate.DayNumber);
        await _context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1})",
            parameters: new object[] { QueueNumberLockClass, queueKey2 },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// محاكاة دلالية لـ patient.transferMember على مزوّدات الاختبار (InMemory) دون triggers.
    /// </summary>
    private async Task<Guid> ExecuteTransferInMemoryAsync(
        Guid memberId,
        Guid toCenterId,
        string newCenterMemberId,
        string executedByUserId,
        string transferReason,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken)
    {
        var member = await _context.Members
            .FirstOrDefaultAsync(m => m.MemberId == memberId, cancellationToken)
            ?? throw new InvalidOperationException($"Member {memberId} does not exist");

        var fromCenterId = member.CurrentMedicalCenterId;
        if (fromCenterId == toCenterId)
            throw new InvalidOperationException("Cannot transfer member to the same medical center");

        var openIdentity = await _context.MemberCenterIdentities
            .FirstOrDefaultAsync(
                i => i.MemberId == memberId
                     && i.MedicalCenterId == fromCenterId
                     && i.ValidTo == null,
                cancellationToken)
            ?? throw new InvalidOperationException("Current open memberCenterIdentity was not found");

        openIdentity.ValidTo = executedAt;
        openIdentity.ClosedByUserId = executedByUserId;

        var toIdentityId = Guid.NewGuid();
        _context.MemberCenterIdentities.Add(new MemberCenterIdentity
        {
            MemberCenterIdentityId = toIdentityId,
            MemberId = memberId,
            MedicalCenterId = toCenterId,
            CenterMemberId = newCenterMemberId,
            ValidFrom = executedAt,
            ValidTo = null,
            AssignedByUserId = executedByUserId,
            ClosedByUserId = null
        });

        // Matches SQL transferMember: center change + export reset; queue origin stays historical.
        member.CurrentMedicalCenterId = toCenterId;
        member.DataRevision += 1;
        member.ExportStatus = "UNEXPORTED";
        member.LastExportedAt = null;
        member.UpdatedByUserId = executedByUserId;
        member.UpdatedAt = executedAt;

        var transferId = Guid.NewGuid();
        _context.MemberTransfers.Add(new MemberTransfer
        {
            TransferId = transferId,
            MemberId = memberId,
            FromCenterId = fromCenterId,
            ToCenterId = toCenterId,
            FromMemberCenterIdentityId = openIdentity.MemberCenterIdentityId,
            ToMemberCenterIdentityId = toIdentityId,
            TransferReason = transferReason,
            ExecutedByUserId = executedByUserId,
            ExecutedAt = executedAt
        });

        await _context.SaveChangesAsync(cancellationToken);
        return transferId;
    }

    /// <inheritdoc />
    public bool MaterialMemberRevisionManagedByDatabase()
        => IsRelationalProvider() && IsNpgsqlProvider();

    /// <inheritdoc />
    public async Task<int> MarkExportedForRecruitmentAsync(
        Guid centerId,
        IReadOnlyCollection<Guid> memberIds,
        string updatedByUserId,
        DateTimeOffset exportedAt,
        CancellationToken cancellationToken = default)
    {
        if (memberIds.Count == 0)
            return 0;

        var query = _context.Members
            .Where(m => memberIds.Contains(m.MemberId) && m.CurrentMedicalCenterId == centerId);

        // PostgreSQL: تحديث أعمدة التصدير فقط؛ prepareMemberRevision يقارن قائمة صريحة من الأعمدة المادية
        // (بدون bmi المولَّد وحقول التصدير/المراجعة/التدقيق).
        // InMemory/غير العلائقي: مسار مكافئ عبر ChangeTracker (لا محفزات مراجعة).
        if (!IsRelationalProvider())
        {
            var members = await query.ToListAsync(cancellationToken);
            foreach (var member in members)
            {
                member.ExportStatus = "EXPORTED";
                member.LastExportedRevision = member.DataRevision;
                member.LastExportedAt = exportedAt;
                member.UpdatedAt = exportedAt;
                member.UpdatedByUserId = updatedByUserId;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return members.Count;
        }

        return await query.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(m => m.ExportStatus, "EXPORTED")
                .SetProperty(m => m.LastExportedRevision, m => m.DataRevision)
                .SetProperty(m => m.LastExportedAt, exportedAt)
                .SetProperty(m => m.UpdatedAt, exportedAt)
                .SetProperty(m => m.UpdatedByUserId, updatedByUserId),
            cancellationToken);
    }

    private bool IsNpgsqlProvider()
        => _context.Database.ProviderName is { } name
           && name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

    private bool IsRelationalProvider()
        => _context.Database.IsRelational();

    private static IQueryable<Member> IncludeLookups(IQueryable<Member> query)
        => query
            .Include(m => m.MaritalStatus)
            .Include(m => m.RecruitmentCenter)
            .Include(m => m.BloodType)
            .Include(m => m.CurrentMedicalCenter);
}
