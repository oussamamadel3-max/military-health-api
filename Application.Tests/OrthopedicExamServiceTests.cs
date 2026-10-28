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

public class OrthopedicExamServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicOrtho = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Save_accepted_persists_musculoskeletal_and_neurosurgery()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-OK");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var saved = await service.SaveAsync(memberId, AcceptedOrthopedicRequest());

        Assert.Equal((short)1, saved.ResultStatusId);
        Assert.Equal(1, saved.ExamVersionNo);
        Assert.Equal("أطراف وعمود فقري ومفاصل طبيعية", saved.Musculoskeletal);
        Assert.Equal("حركة وإحساس طبيعيان", saved.Neurosurgery);
        Assert.Equal("actor-1", saved.ExaminerUserId);
        Assert.Equal(CenterA, saved.CenterId);

        Assert.Equal(1, await db.OrthopedicExamDetails.CountAsync());
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(0, await db.ClinicExamEditLocks.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == OrthopedicExamService.OrthopedicExamSavedEventType && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Save_rejects_missing_required_section()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-REQ");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedOrthopedicRequest();
        request.Musculoskeletal = "  ";

        var error = await Assert.ThrowsAsync<InvalidOrthopedicExamDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Contains("العضلي", error.Message);
        Assert.Equal(0, await db.OrthopedicExamDetails.CountAsync());
        Assert.Equal(1, await db.ClinicExamEditLocks.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_non_orthopedic_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "ORT-EYE");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedOrthopedicRequest();
        request.ClinicId = ClinicEyes;

        await Assert.ThrowsAsync<InvalidOrthopedicExamDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Equal(0, await db.OrthopedicExamDetails.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_unassigned_doctor()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-DOC");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-2");
        await SeedUserAsync(db, "actor-2", "actor-2");
        var service = CreateService(db, CenterA, "actor-2");

        await Assert.ThrowsAsync<DoctorClinicNotAssignedException>(() =>
            service.SaveAsync(memberId, AcceptedOrthopedicRequest()));

        Assert.Equal(0, await db.OrthopedicExamDetails.CountAsync());
    }

    [Fact]
    public async Task Get_rejects_doctor_from_other_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedDoctorAssignmentAsync(db, "eye-doc", CenterA, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "ORT-XCL");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var orthoService = CreateService(db, CenterA, "actor-1");
        await orthoService.SaveAsync(memberId, AcceptedOrthopedicRequest());

        var eyeDoctorService = CreateService(db, CenterA, "eye-doc");

        await Assert.ThrowsAsync<DoctorClinicNotAssignedException>(() =>
            eyeDoctorService.GetAsync(memberId, ClinicOrtho));
    }

    [Fact]
    public async Task Save_rejected_without_reason_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-REJ");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedOrthopedicRequest();
        request.ResultStatusId = 2;
        request.RejectionReason = null;

        await Assert.ThrowsAsync<InvalidClinicAssessmentDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Equal(0, await db.OrthopedicExamDetails.CountAsync());
    }

    [Fact]
    public async Task Save_deferred_with_action_persists()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-DEF-OK");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedOrthopedicRequest();
        request.ResultStatusId = 3;
        request.DeferReason = "يحتاج أشعة";
        request.ActionTypeId = 1;
        request.FollowupNote = "متابعة";
        request.ActionDescription = "طلب تقرير عظمي";

        var saved = await service.SaveAsync(memberId, request);

        Assert.Equal((short)3, saved.ResultStatusId);
        Assert.NotNull(saved.DeferredActionId);
        Assert.Equal("REQUESTED", Assert.Single(await db.DeferredActions.ToListAsync()).Status);
    }

    [Fact]
    public async Task Save_deferred_without_action_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-DEF");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedOrthopedicRequest();
        request.ResultStatusId = 3;
        request.DeferReason = "سبب";
        request.FollowupNote = "ملاحظة";

        await Assert.ThrowsAsync<InvalidClinicAssessmentDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Equal(0, await db.DeferredActions.CountAsync());
    }

    [Fact]
    public async Task Save_updates_version_in_place()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-UPD");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedOrthopedicRequest());

        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var update = AcceptedOrthopedicRequest();
        update.Musculoskeletal = "محدودية حركة الركبة";
        update.ResultStatusId = 2;
        update.RejectionReason = "إصابة مفصلية";

        var second = await service.SaveAsync(memberId, update);

        Assert.Equal(first.ExamId, second.ExamId);
        Assert.Equal(2, second.ExamVersionNo);
        Assert.Equal("محدودية حركة الركبة", second.Musculoskeletal);
        Assert.Equal((short)2, second.ResultStatusId);
        Assert.Equal(1, await db.OrthopedicExamDetails.CountAsync());
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(2, await db.AuditEvents.CountAsync(e =>
            e.EventType == OrthopedicExamService.OrthopedicExamSavedEventType && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Save_without_lock_is_conflict()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-NOLK");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<ClinicExamEditLockConflictException>(() =>
            service.SaveAsync(memberId, AcceptedOrthopedicRequest()));

        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == OrthopedicExamService.ExamConflictEventType && e.Result == "FAILURE"));
    }

    [Fact]
    public async Task Get_returns_saved_exam()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-GET");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");
        await service.SaveAsync(memberId, AcceptedOrthopedicRequest());

        var got = await service.GetAsync(memberId, ClinicOrtho);

        Assert.Equal("أطراف وعمود فقري ومفاصل طبيعية", got.Musculoskeletal);
        Assert.Equal("حركة وإحساس طبيعيان", got.Neurosurgery);
        Assert.Equal((short)1, got.ResultStatusId);
    }

    [Fact]
    public async Task Get_missing_exam_is_not_found()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-MISS");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<OrthopedicExamNotFoundException>(() =>
            service.GetAsync(memberId, ClinicOrtho));
    }

    [Fact]
    public async Task List_returns_current_orthopedic_exams()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-LIST");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");
        await service.SaveAsync(memberId, AcceptedOrthopedicRequest());

        var page = await service.ListAsync(new ClinicExamListQuery { ClinicId = ClinicOrtho });

        var item = Assert.Single(page.Items);
        Assert.Equal(memberId, item.MemberId);
        Assert.Equal("أطراف وعمود فقري ومفاصل طبيعية", item.Musculoskeletal);
        Assert.Equal("حركة وإحساس طبيعيان", item.Neurosurgery);
        Assert.Equal((short)1, item.ResultStatusId);
        Assert.Equal("مقبول", item.ResultStatusNameAr);
    }

    [Fact]
    public async Task Save_second_pass_keeps_single_current_exam()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ORT-DUP");
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedOrthopedicRequest());
        await AcquireLockAsync(db, memberId, ClinicOrtho, "actor-1");
        var second = await service.SaveAsync(memberId, AcceptedOrthopedicRequest());

        Assert.Equal(first.ExamId, second.ExamId);
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(1, await db.OrthopedicExamDetails.CountAsync());
        Assert.Equal(2, second.ExamVersionNo);
    }

    private static OrthopedicExamService CreateService(AppDbContext db, Guid centerId, string userId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser(userId, userId, "127.0.0.1");

        return new OrthopedicExamService(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new OrthopedicExamTestUnitOfWork(db),
            gate.Object,
            currentUser);
    }

    private static SaveOrthopedicExamRequest AcceptedOrthopedicRequest()
        => new()
        {
            ClinicId = ClinicOrtho,
            Musculoskeletal = "أطراف وعمود فقري ومفاصل طبيعية",
            Neurosurgery = "حركة وإحساس طبيعيان",
            ResultStatusId = 1
        };

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicOrtho, "ORTHOPEDIC", "العظمية", 5);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedCenterClinicAsync(db, CenterA, ClinicOrtho);
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
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
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicOrtho);
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب عظمية",
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

    private sealed class OrthopedicExamTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public OrthopedicExamTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new OrthopedicExamNoopTransaction());
    }

    private sealed class OrthopedicExamNoopTransaction : IDbContextTransaction
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
