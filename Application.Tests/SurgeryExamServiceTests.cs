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

public class SurgeryExamServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicSurgery = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Save_accepted_persists_all_surgery_sections()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-OK");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var saved = await service.SaveAsync(memberId, AcceptedSurgeryRequest());

        Assert.Equal((short)1, saved.ResultStatusId);
        Assert.Equal(1, saved.ExamVersionNo);
        Assert.Equal("طبيعي", saved.GeneralSurgery);
        Assert.Equal("طبيعي", saved.Urology);
        Assert.Equal("طبيعي", saved.VascularThoracic);
        Assert.Equal("actor-1", saved.ExaminerUserId);
        Assert.Equal(CenterA, saved.CenterId);

        Assert.Equal(1, await db.SurgeryExamDetails.CountAsync());
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(0, await db.ClinicExamEditLocks.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == SurgeryExamService.SurgeryExamSavedEventType && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Save_rejects_missing_required_section()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-REQ");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedSurgeryRequest();
        request.GeneralSurgery = "  ";

        var error = await Assert.ThrowsAsync<InvalidSurgeryExamDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Contains("العامة", error.Message);
        Assert.Equal(0, await db.SurgeryExamDetails.CountAsync());
        Assert.Equal(1, await db.ClinicExamEditLocks.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_non_surgery_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "SUR-EYE");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedSurgeryRequest();
        request.ClinicId = ClinicEyes;

        await Assert.ThrowsAsync<InvalidSurgeryExamDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Equal(0, await db.SurgeryExamDetails.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_unassigned_doctor()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-DOC");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-2");
        await SeedUserAsync(db, "actor-2", "actor-2");
        var service = CreateService(db, CenterA, "actor-2");

        await Assert.ThrowsAsync<DoctorClinicNotAssignedException>(() =>
            service.SaveAsync(memberId, AcceptedSurgeryRequest()));

        Assert.Equal(0, await db.SurgeryExamDetails.CountAsync());
    }

    [Fact]
    public async Task Save_rejected_without_reason_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-REJ");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedSurgeryRequest();
        request.ResultStatusId = 2;
        request.RejectionReason = null;

        await Assert.ThrowsAsync<InvalidClinicAssessmentDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Equal(0, await db.SurgeryExamDetails.CountAsync());
    }

    [Fact]
    public async Task Save_rejected_with_reason_persists()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-REJ-OK");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedSurgeryRequest();
        request.ResultStatusId = 2;
        request.RejectionReason = "فتق إربي";

        var saved = await service.SaveAsync(memberId, request);

        Assert.Equal((short)2, saved.ResultStatusId);
        Assert.Equal("فتق إربي", saved.RejectionReason);
    }

    [Fact]
    public async Task Save_deferred_without_action_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-DEF");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedSurgeryRequest();
        request.ResultStatusId = 3;
        request.DeferReason = "سبب";
        request.FollowupNote = "ملاحظة";

        await Assert.ThrowsAsync<InvalidClinicAssessmentDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Equal(0, await db.DeferredActions.CountAsync());
    }

    [Fact]
    public async Task Save_deferred_creates_action()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-DEF-OK");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedSurgeryRequest();
        request.ResultStatusId = 3;
        request.DeferReason = "يحتاج استشارة";
        request.ActionTypeId = 1;
        request.FollowupNote = "متابعة";
        request.ActionDescription = "طلب تقرير جراحي";

        var saved = await service.SaveAsync(memberId, request);

        Assert.Equal((short)3, saved.ResultStatusId);
        Assert.NotNull(saved.DeferredActionId);
        Assert.Equal("REQUESTED", Assert.Single(await db.DeferredActions.ToListAsync()).Status);
    }

    [Fact]
    public async Task Save_updates_version_in_place()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-UPD");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedSurgeryRequest());

        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var update = AcceptedSurgeryRequest();
        update.Urology = "حصاة كلوية";
        update.ResultStatusId = 2;
        update.RejectionReason = "مشكلة بولية";

        var second = await service.SaveAsync(memberId, update);

        Assert.Equal(first.ExamId, second.ExamId);
        Assert.Equal(2, second.ExamVersionNo);
        Assert.Equal("حصاة كلوية", second.Urology);
        Assert.Equal((short)2, second.ResultStatusId);
        Assert.Equal(1, await db.SurgeryExamDetails.CountAsync());
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(2, await db.AuditEvents.CountAsync(e =>
            e.EventType == SurgeryExamService.SurgeryExamSavedEventType && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Save_without_lock_is_conflict()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-NOLK");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<ClinicExamEditLockConflictException>(() =>
            service.SaveAsync(memberId, AcceptedSurgeryRequest()));

        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == SurgeryExamService.ExamConflictEventType && e.Result == "FAILURE"));
    }

    [Fact]
    public async Task Get_returns_saved_exam()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-GET");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");
        await service.SaveAsync(memberId, AcceptedSurgeryRequest());

        var got = await service.GetAsync(memberId, ClinicSurgery);

        Assert.Equal("طبيعي", got.GeneralSurgery);
        Assert.Equal("طبيعي", got.VascularThoracic);
        Assert.Equal((short)1, got.ResultStatusId);
    }

    [Fact]
    public async Task Get_missing_exam_is_not_found()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-MISS");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<SurgeryExamNotFoundException>(() =>
            service.GetAsync(memberId, ClinicSurgery));
    }

    [Fact]
    public async Task List_returns_current_surgery_exams()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-LIST");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");
        await service.SaveAsync(memberId, AcceptedSurgeryRequest());

        var page = await service.ListAsync(new ClinicExamListQuery { ClinicId = ClinicSurgery });

        var item = Assert.Single(page.Items);
        Assert.Equal(memberId, item.MemberId);
        Assert.Equal("طبيعي", item.GeneralSurgery);
        Assert.Equal("طبيعي", item.Urology);
        Assert.Equal((short)1, item.ResultStatusId);
        Assert.Equal("مقبول", item.ResultStatusNameAr);
    }

    [Fact]
    public async Task Save_second_pass_keeps_single_current_exam()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "SUR-DUP");
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedSurgeryRequest());
        await AcquireLockAsync(db, memberId, ClinicSurgery, "actor-1");
        var second = await service.SaveAsync(memberId, AcceptedSurgeryRequest());

        Assert.Equal(first.ExamId, second.ExamId);
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(1, await db.SurgeryExamDetails.CountAsync());
        Assert.Equal(2, second.ExamVersionNo);
    }

    private static SurgeryExamService CreateService(AppDbContext db, Guid centerId, string userId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser(userId, userId, "127.0.0.1");

        return new SurgeryExamService(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new SurgeryExamTestUnitOfWork(db),
            gate.Object,
            currentUser);
    }

    private static SaveSurgeryExamRequest AcceptedSurgeryRequest()
        => new()
        {
            ClinicId = ClinicSurgery,
            GeneralSurgery = "طبيعي",
            Urology = "طبيعي",
            VascularThoracic = "طبيعي",
            ResultStatusId = 1
        };

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicSurgery, "SURGERY", "الجراحة", 4);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedCenterClinicAsync(db, CenterA, ClinicSurgery);
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
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicSurgery);
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب جراحة",
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

    private sealed class SurgeryExamTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public SurgeryExamTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new SurgeryExamNoopTransaction());
    }

    private sealed class SurgeryExamNoopTransaction : IDbContextTransaction
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
