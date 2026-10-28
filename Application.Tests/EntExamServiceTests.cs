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

public class EntExamServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Save_accepted_persists_all_ent_sections_and_examiner()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-OK");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var saved = await service.SaveAsync(memberId, AcceptedEntRequest());

        Assert.Equal((short)1, saved.ResultStatusId);
        Assert.Equal(1, saved.ExamVersionNo);
        Assert.Equal("طبيعي يمنى", saved.RightEar);
        Assert.Equal("طبيعي يسرى", saved.LeftEar);
        Assert.Equal("سليم", saved.TympanicMembrane);
        Assert.Equal("طبيعي", saved.HearingTest);
        Assert.Equal("مسموع", saved.WhisperTest);
        Assert.Equal("طبيعي", saved.NoseExam);
        Assert.Equal("طبيعي", saved.MouthExam);
        Assert.Equal("لا يوجد", saved.OtherEntDiseases);
        Assert.Equal("actor-1", saved.ExaminerUserId);
        Assert.Equal(CenterA, saved.CenterId);

        Assert.Equal(1, await db.EntExamDetails.CountAsync());
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(0, await db.ClinicExamEditLocks.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == EntExamService.EntExamSavedEventType && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Save_allows_optional_other_diseases_null()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-OTH");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEntRequest();
        request.OtherEntDiseases = null;

        var saved = await service.SaveAsync(memberId, request);

        Assert.Null(saved.OtherEntDiseases);
        Assert.Equal(1, await db.EntExamDetails.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_missing_right_ear()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-REQ");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEntRequest();
        request.RightEar = "  ";

        var error = await Assert.ThrowsAsync<InvalidEntExamDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Contains("اليمنى", error.Message);
        Assert.Equal(0, await db.EntExamDetails.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_non_ent_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "ENT-EYE");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEntRequest();
        request.ClinicId = ClinicEyes;

        await Assert.ThrowsAsync<InvalidEntExamDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Equal(0, await db.EntExamDetails.CountAsync());
    }

    [Fact]
    public async Task Get_rejects_doctor_from_other_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedDoctorAssignmentAsync(db, "eye-doc", CenterA, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "ENT-XCL");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var entService = CreateService(db, CenterA, "actor-1");
        await entService.SaveAsync(memberId, AcceptedEntRequest());

        var eyeDoctorService = CreateService(db, CenterA, "eye-doc");

        await Assert.ThrowsAsync<DoctorClinicNotAssignedException>(() =>
            eyeDoctorService.GetAsync(memberId, ClinicEnt));
    }

    [Fact]
    public async Task Save_rejected_with_reason_persists()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-REJ");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEntRequest();
        request.ResultStatusId = 2;
        request.RejectionReason = "ضعف سمع";

        var saved = await service.SaveAsync(memberId, request);

        Assert.Equal((short)2, saved.ResultStatusId);
        Assert.Equal("ضعف سمع", saved.RejectionReason);
        Assert.Equal("actor-1", saved.ExaminerUserId);
    }

    [Fact]
    public async Task Save_deferred_with_action_persists()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-DEF");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEntRequest();
        request.ResultStatusId = 3;
        request.DeferReason = "يحتاج قياس سمع";
        request.ActionTypeId = 1;
        request.FollowupNote = "متابعة";
        request.ActionDescription = "طلب تقرير سمعي";

        var saved = await service.SaveAsync(memberId, request);

        Assert.Equal((short)3, saved.ResultStatusId);
        Assert.NotNull(saved.DeferredActionId);
        Assert.Equal("REQUESTED", Assert.Single(await db.DeferredActions.ToListAsync()).Status);
    }

    [Fact]
    public async Task Save_updates_version_and_ear_values_in_place()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-UPD");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedEntRequest());

        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var update = AcceptedEntRequest();
        update.RightEar = "التهاب يمنى";
        update.LeftEar = "سليم يسرى";
        update.ResultStatusId = 2;
        update.RejectionReason = "التهاب أذن";

        var second = await service.SaveAsync(memberId, update);

        Assert.Equal(first.ExamId, second.ExamId);
        Assert.Equal(2, second.ExamVersionNo);
        Assert.Equal("التهاب يمنى", second.RightEar);
        Assert.Equal("سليم يسرى", second.LeftEar);
        Assert.Equal(1, await db.EntExamDetails.CountAsync());
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
    }

    [Fact]
    public async Task Save_without_lock_is_conflict()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-NOLK");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<ClinicExamEditLockConflictException>(() =>
            service.SaveAsync(memberId, AcceptedEntRequest()));

        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == EntExamService.ExamConflictEventType && e.Result == "FAILURE"));
    }

    [Fact]
    public async Task Get_returns_saved_exam()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-GET");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");
        await service.SaveAsync(memberId, AcceptedEntRequest());

        var got = await service.GetAsync(memberId, ClinicEnt);

        Assert.Equal("طبيعي يمنى", got.RightEar);
        Assert.Equal("طبيعي يسرى", got.LeftEar);
        Assert.Equal((short)1, got.ResultStatusId);
        Assert.Equal("actor-1", got.ExaminerUserId);
    }

    [Fact]
    public async Task Get_missing_exam_is_not_found()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-MISS");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<EntExamNotFoundException>(() =>
            service.GetAsync(memberId, ClinicEnt));
    }

    [Fact]
    public async Task List_returns_current_ent_exams()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-LIST");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");
        await service.SaveAsync(memberId, AcceptedEntRequest());

        var page = await service.ListAsync(new ClinicExamListQuery { ClinicId = ClinicEnt });

        var item = Assert.Single(page.Items);
        Assert.Equal(memberId, item.MemberId);
        Assert.Equal("طبيعي يمنى", item.RightEar);
        Assert.Equal("طبيعي يسرى", item.LeftEar);
        Assert.Equal((short)1, item.ResultStatusId);
        Assert.Equal("مقبول", item.ResultStatusNameAr);
    }

    [Fact]
    public async Task Save_second_pass_keeps_single_current_exam()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-DUP");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedEntRequest());
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var second = await service.SaveAsync(memberId, AcceptedEntRequest());

        Assert.Equal(first.ExamId, second.ExamId);
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(1, await db.EntExamDetails.CountAsync());
        Assert.Equal(2, second.ExamVersionNo);
    }

    [Fact]
    public async Task Save_rejects_unassigned_doctor()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "ENT-DOC");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-2");
        await SeedUserAsync(db, "actor-2", "actor-2");
        var service = CreateService(db, CenterA, "actor-2");

        await Assert.ThrowsAsync<DoctorClinicNotAssignedException>(() =>
            service.SaveAsync(memberId, AcceptedEntRequest()));

        Assert.Equal(0, await db.EntExamDetails.CountAsync());
    }

    private static EntExamService CreateService(AppDbContext db, Guid centerId, string userId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser(userId, userId, "127.0.0.1");

        return new EntExamService(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new EntExamTestUnitOfWork(db),
            gate.Object,
            currentUser);
    }

    private static SaveEntExamRequest AcceptedEntRequest()
        => new()
        {
            ClinicId = ClinicEnt,
            RightEar = "طبيعي يمنى",
            LeftEar = "طبيعي يسرى",
            TympanicMembrane = "سليم",
            HearingTest = "طبيعي",
            WhisperTest = "مسموع",
            NoseExam = "طبيعي",
            MouthExam = "طبيعي",
            OtherEntDiseases = "لا يوجد",
            ResultStatusId = 1
        };

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedCenterClinicAsync(db, CenterA, ClinicEnt);
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
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEnt);
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب أذنية",
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

    private sealed class EntExamTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public EntExamTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new EntExamNoopTransaction());
    }

    private sealed class EntExamNoopTransaction : IDbContextTransaction
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
