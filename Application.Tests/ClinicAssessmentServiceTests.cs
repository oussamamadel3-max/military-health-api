using Application.Common.Permissions;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.ClinicWorkspace;
using Domain.Entities.Auth;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class ClinicAssessmentServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Save_accepted_creates_exam_assessment_and_releases_lock()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ACC-1");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var saved = await service.SaveAsync(memberId, AcceptedRequest(ClinicEyes));

        Assert.Equal(1, saved.ResultStatusId);
        Assert.Null(saved.DeferredActionId);
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.IsCurrent));
        Assert.Equal(1, await db.ClinicAssessments.CountAsync());
        Assert.Equal(0, await db.DeferredActions.CountAsync());
        Assert.Equal(0, await db.ClinicExamEditLocks.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == ClinicAssessmentService.AssessmentSavedEventType && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Save_rejected_without_reason_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "REJ-1");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<InvalidClinicAssessmentDataException>(() =>
            service.SaveAsync(memberId, new SaveClinicAssessmentRequest
            {
                ClinicId = ClinicEyes,
                ResultStatusId = 2
            }));

        Assert.Equal(0, await db.ClinicAssessments.CountAsync());
        Assert.Equal(1, await db.ClinicExamEditLocks.CountAsync());
    }

    [Fact]
    public async Task Save_deferred_without_action_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "DEF-MISS");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<InvalidClinicAssessmentDataException>(() =>
            service.SaveAsync(memberId, new SaveClinicAssessmentRequest
            {
                ClinicId = ClinicEyes,
                ResultStatusId = 3,
                DeferReason = "سبب",
                FollowupNote = "ملاحظة"
            }));

        Assert.Equal(0, await db.DeferredActions.CountAsync());
    }

    [Fact]
    public async Task Save_deferred_creates_requested_action()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "DEF-OK");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var saved = await service.SaveAsync(memberId, DeferredRequest(ClinicEyes));

        Assert.Equal(3, saved.ResultStatusId);
        Assert.NotNull(saved.DeferredActionId);
        var action = Assert.Single(await db.DeferredActions.ToListAsync());
        Assert.Equal("REQUESTED", action.Status);
        Assert.Equal(saved.AssessmentId, action.ClinicAssessmentId);
        Assert.Equal("يحتاج تقرير", action.DeferReason);
        Assert.Equal("طلب تقرير طبي", action.ActionDescription);
        Assert.Equal(0, await db.ClinicExamEditLocks.CountAsync());
    }

    [Fact]
    public async Task Save_without_lock_is_conflict_and_audited()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "NO-LOCK");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<ClinicExamEditLockConflictException>(() =>
            service.SaveAsync(memberId, AcceptedRequest(ClinicEyes)));

        Assert.Equal(0, await db.ClinicAssessments.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == ClinicAssessmentService.ExamConflictEventType && e.Result == "FAILURE"));
    }

    [Fact]
    public async Task Save_with_other_doctor_lock_is_conflict()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedUserAsync(db, "doc-2", "doctor2");
        await SeedDoctorAssignmentAsync(db, "doc-2", CenterA, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "OTH-LOCK");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "doc-2");

        await Assert.ThrowsAsync<ClinicExamEditLockConflictException>(() =>
            service.SaveAsync(memberId, AcceptedRequest(ClinicEyes)));

        Assert.Equal(1, await db.ClinicExamEditLocks.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == ClinicAssessmentService.ExamConflictEventType && e.Result == "FAILURE"));
    }

    [Fact]
    public async Task Save_expired_lock_is_conflict()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EXP-LOCK");
        db.ClinicExamEditLocks.Add(new ClinicExamEditLock
        {
            LockId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            LockedByUserId = "actor-1",
            LockedAt = DateTimeOffset.UtcNow.AddMinutes(-40),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<ClinicExamEditLockConflictException>(() =>
            service.SaveAsync(memberId, AcceptedRequest(ClinicEyes)));
    }

    [Fact]
    public async Task Save_two_clinics_same_member_succeeds()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEnt);
        await SeedUserAsync(db, "doc-2", "doctor2");
        await SeedDoctorAssignmentAsync(db, "doc-2", CenterA, ClinicEnt);
        var memberId = await CreateMemberAsync(db, "TWO-CL");

        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var eyes = CreateService(db, CenterA, "actor-1");
        await eyes.SaveAsync(memberId, AcceptedRequest(ClinicEyes));

        await AcquireLockAsync(db, memberId, ClinicEnt, "doc-2");
        var ent = CreateService(db, CenterA, "doc-2");
        await ent.SaveAsync(memberId, AcceptedRequest(ClinicEnt));

        Assert.Equal(2, await db.MedicalExams.CountAsync(e => e.IsCurrent && e.MemberId == memberId));
        Assert.Equal(2, await db.ClinicAssessments.CountAsync(a => a.MemberId == memberId));
    }

    [Fact]
    public async Task Save_updates_existing_current_exam_version()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "UPD-1");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedRequest(ClinicEyes));
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var second = await service.SaveAsync(memberId, new SaveClinicAssessmentRequest
        {
            ClinicId = ClinicEyes,
            ResultStatusId = 2,
            RejectionReason = "غير لائق"
        });

        Assert.Equal(first.ExamId, second.ExamId);
        Assert.Equal(first.AssessmentId, second.AssessmentId);
        Assert.Equal(2, second.ExamVersionNo);
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId));
        Assert.Equal(2, Assert.Single(await db.ClinicAssessments.ToListAsync()).ResultStatusId);
    }

    [Fact]
    public async Task Release_lock_removes_owned_lock()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "REL-1");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        await service.ReleaseEditLockAsync(memberId, ClinicEyes);

        Assert.Equal(0, await db.ClinicExamEditLocks.CountAsync());
    }

    [Fact]
    public async Task Release_lock_does_not_steal_other_doctor_lock()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedUserAsync(db, "doc-2", "doctor2");
        await SeedDoctorAssignmentAsync(db, "doc-2", CenterA, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "REL-2");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "doc-2");

        await Assert.ThrowsAsync<ClinicExamEditLockConflictException>(() =>
            service.ReleaseEditLockAsync(memberId, ClinicEyes));

        Assert.Equal("actor-1", Assert.Single(await db.ClinicExamEditLocks.ToListAsync()).LockedByUserId);
    }

    private static ClinicAssessmentService CreateService(AppDbContext db, Guid centerId, string userId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser(userId, userId, "127.0.0.1");

        var memberService = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);

        return new ClinicAssessmentService(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            memberService,
            new AuditLogRepository(db),
            new AssessmentTestUnitOfWork(db),
            gate.Object,
            currentUser);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedCenterClinicAsync(db, CenterA, ClinicEnt);
        db.ResultStatuses.AddRange(
            new ResultStatus { StatusId = 1, Code = "ACCEPTED", NameAr = "مقبول", IsActive = true },
            new ResultStatus { StatusId = 2, Code = "REJECTED", NameAr = "مرفوض", IsActive = true },
            new ResultStatus { StatusId = 3, Code = "DEFERRED", NameAr = "مؤجل", IsActive = true });
        db.RequiredActionTypes.Add(new RequiredActionType
        {
            ActionTypeId = 1,
            Code = "REPORT",
            NameAr = "تقرير طبي",
            RequiresDescription = true,
            RequiresTargetClinic = false,
            IsActive = true
        });
        await db.SaveChangesAsync();
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEyes);
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب تقييم",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = recruitment,
            RecruitmentCenterId = RecruitmentCenterId,
            BloodTypeId = 1,
            HeightCm = 170,
            WeightKg = 66,
            BloodPressure = "120/80",
            PulseBpm = 72,
            HasTattoo = false
        });
        return created.MemberId;
    }

    private static async Task AcquireLockAsync(AppDbContext db, Guid memberId, Guid clinicId, string userId)
    {
        var existing = await db.ClinicExamEditLocks
            .FirstOrDefaultAsync(l => l.MemberId == memberId && l.ClinicId == clinicId);
        if (existing is not null)
            db.ClinicExamEditLocks.Remove(existing);

        var now = DateTimeOffset.UtcNow;
        db.ClinicExamEditLocks.Add(new ClinicExamEditLock
        {
            LockId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = CenterA,
            ClinicId = clinicId,
            LockedByUserId = userId,
            LockedAt = now,
            ExpiresAt = now.AddMinutes(30)
        });
        await db.SaveChangesAsync();
    }

    private static SaveClinicAssessmentRequest AcceptedRequest(Guid clinicId)
        => new()
        {
            ClinicId = clinicId,
            ResultStatusId = 1
        };

    private static SaveClinicAssessmentRequest DeferredRequest(Guid clinicId)
        => new()
        {
            ClinicId = clinicId,
            ResultStatusId = 3,
            DeferReason = "يحتاج تقرير",
            ActionTypeId = 1,
            FollowupNote = "متابعة بعد التقرير",
            ActionDescription = "طلب تقرير طبي"
        };

    private static async Task SeedClinicAsync(AppDbContext db, Guid clinicId, string code, string nameAr, int sort)
    {
        if (await db.Clinics.AnyAsync(c => c.ClinicId == clinicId))
            return;

        db.Clinics.Add(new Clinic
        {
            ClinicId = clinicId,
            Code = code,
            NameAr = nameAr,
            SortOrder = (short)sort,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCenterClinicAsync(AppDbContext db, Guid centerId, Guid clinicId)
    {
        if (await db.CenterClinics.AnyAsync(x => x.CenterId == centerId && x.ClinicId == clinicId))
            return;

        db.CenterClinics.Add(new CenterClinic
        {
            CenterClinicId = Guid.NewGuid(),
            CenterId = centerId,
            ClinicId = clinicId,
            IsActive = true,
            ActivatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedDoctorAssignmentAsync(AppDbContext db, string userId, Guid centerId, Guid clinicId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId))
            await SeedUserAsync(db, userId, userId);

        if (await db.UserClinicAssignments.AnyAsync(a =>
                a.UserId == userId && a.CenterId == centerId && a.ClinicId == clinicId && a.IsActive))
            return;

        db.UserClinicAssignments.Add(new UserClinicAssignment
        {
            AssignmentId = Guid.NewGuid(),
            UserId = userId,
            CenterId = centerId,
            ClinicId = clinicId,
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = "actor-1"
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedUserAsync(AppDbContext db, string userId, string userName)
    {
        if (await db.Users.AnyAsync(u => u.Id == userId))
            return;

        db.Users.Add(new User
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@test.local",
            NormalizedEmail = $"{userName.ToUpperInvariant()}@TEST.LOCAL",
            SecurityStamp = Guid.NewGuid().ToString(),
            IsActive = true,
            FullName = userName
        });
        await db.SaveChangesAsync();
    }

    private sealed class AssessmentTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public AssessmentTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new AssessmentNoopTransaction());
    }

    private sealed class AssessmentNoopTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
