using Application.Common.Options;
using Application.Common.Permissions;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.ClinicWorkspace;
using Application.Services.Members;
using Domain.Entities.Auth;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Moq;

namespace Application.Tests;

public class ClinicWorkspaceServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Search_by_queue_number_returns_center_member()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(ValidCreate("Q-1"));
        var workspace = CreateWorkspace(db, CenterA, "actor-1");

        var page = await workspace.SearchMembersAsync(
            ClinicEyes,
            new MemberListQuery { QueueNumber = created.QueueNumber });

        var hit = Assert.Single(page.Items);
        Assert.Equal(created.MemberId, hit.MemberId);
        Assert.Equal(ClinicExamStatusCodes.NotRegistered, hit.ClinicExamStatus);
    }

    [Fact]
    public async Task Search_hides_member_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        var membersB = MemberServiceTests.CreateService(db, CenterB, allowCreate: true, allowView: true);
        await membersB.CreateAsync(ValidCreate("OTHER-1"));
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEyes);
        var workspace = CreateWorkspace(db, CenterA, "actor-1");

        var page = await workspace.SearchMembersAsync(
            ClinicEyes,
            new MemberListQuery { RecruitmentNumber = "OTHER-1" });

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Search_rejects_unassigned_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db, assignDoctorToEyes: false);
        var workspace = CreateWorkspace(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<DoctorClinicNotAssignedException>(() =>
            workspace.SearchMembersAsync(ClinicEyes, new MemberListQuery()));
    }

    [Fact]
    public async Task ListMyClinics_returns_only_active_assignments_in_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEnt);
        var workspace = CreateWorkspace(db, CenterA, "actor-1");

        var clinics = await workspace.ListMyClinicsAsync();

        Assert.Equal(2, clinics.Count);
        Assert.Contains(clinics, c => c.ClinicId == ClinicEyes);
        Assert.Contains(clinics, c => c.ClinicId == ClinicEnt);
    }

    [Fact]
    public async Task Open_acquires_lock_records_audit_and_returns_not_registered()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(ValidCreate("OPEN-1"));
        var workspace = CreateWorkspace(db, CenterA, "actor-1");

        var summary = await workspace.OpenMemberAsync(created.MemberId, ClinicEyes);

        Assert.Equal(ClinicExamStatusCodes.NotRegistered, summary.ClinicExamStatus);
        Assert.True(summary.CanAcquireEditLock);
        Assert.NotNull(summary.EditLock);
        Assert.True(summary.EditLock!.IsOwnedByCurrentUser);
        Assert.Equal(1, await db.ClinicExamEditLocks.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == ClinicWorkspaceService.ClinicMemberViewEventType));
    }

    [Fact]
    public async Task Open_by_second_doctor_returns_in_edit_without_stealing_lock()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        await SeedUserAsync(db, "doc-2", "doctor2");
        await SeedDoctorAssignmentAsync(db, "doc-2", CenterA, ClinicEyes);
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(ValidCreate("LOCK-1"));

        var first = CreateWorkspace(db, CenterA, "actor-1");
        await first.OpenMemberAsync(created.MemberId, ClinicEyes);

        var second = CreateWorkspace(db, CenterA, "doc-2");
        var summary = await second.OpenMemberAsync(created.MemberId, ClinicEyes);

        Assert.Equal(ClinicExamStatusCodes.InEdit, summary.ClinicExamStatus);
        Assert.False(summary.CanAcquireEditLock);
        Assert.Equal("actor-1", summary.EditLock!.LockedByUserId);
        Assert.Equal(1, await db.ClinicExamEditLocks.CountAsync());
    }

    [Fact]
    public async Task Open_after_lock_expiry_allows_second_doctor_to_acquire()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        await SeedUserAsync(db, "doc-2", "doctor2");
        await SeedDoctorAssignmentAsync(db, "doc-2", CenterA, ClinicEyes);
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(ValidCreate("EXP-1"));

        db.ClinicExamEditLocks.Add(new ClinicExamEditLock
        {
            LockId = Guid.NewGuid(),
            MemberId = created.MemberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            LockedByUserId = "actor-1",
            LockedAt = DateTimeOffset.UtcNow.AddMinutes(-40),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        var second = CreateWorkspace(db, CenterA, "doc-2");
        var summary = await second.OpenMemberAsync(created.MemberId, ClinicEyes);

        Assert.NotEqual(ClinicExamStatusCodes.InEdit, summary.ClinicExamStatus);
        Assert.True(summary.CanAcquireEditLock);
        Assert.Equal("doc-2", Assert.Single(await db.ClinicExamEditLocks.ToListAsync()).LockedByUserId);
    }

    [Fact]
    public async Task Open_reports_registered_when_current_exam_exists()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(ValidCreate("REG-1"));
        await SeedCurrentExamAsync(db, created.MemberId, CenterA, ClinicEyes, resultStatusId: 1);
        var workspace = CreateWorkspace(db, CenterA, "actor-1");

        var summary = await workspace.OpenMemberAsync(created.MemberId, ClinicEyes);

        Assert.Equal(ClinicExamStatusCodes.Registered, summary.ClinicExamStatus);
    }

    [Fact]
    public async Task Open_reports_deferred_with_action_summary()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(ValidCreate("DEF-1"));
        var assessmentId = await SeedCurrentExamAsync(db, created.MemberId, CenterA, ClinicEyes, resultStatusId: 3);
        db.DeferredActions.Add(new DeferredAction
        {
            DeferredActionId = Guid.NewGuid(),
            MemberId = created.MemberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            ClinicAssessmentId = assessmentId,
            DeferReason = "يحتاج تقرير",
            ActionTypeId = 1,
            ActionDescription = "طلب تقرير طبي",
            FollowupNote = "متابعة",
            Status = "REQUESTED",
            RequestedByUserId = "actor-1",
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var workspace = CreateWorkspace(db, CenterA, "actor-1");

        var summary = await workspace.OpenMemberAsync(created.MemberId, ClinicEyes);

        Assert.Equal(ClinicExamStatusCodes.Deferred, summary.ClinicExamStatus);
        Assert.NotNull(summary.DeferredSummary);
        Assert.Equal("يحتاج تقرير", summary.DeferredSummary!.DeferReason);
        Assert.Equal("طلب تقرير طبي", summary.DeferredSummary.ActionDescription);
    }

    [Fact]
    public async Task Open_includes_latest_general_note()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(ValidCreate("NOTE-1"));
        db.MemberGeneralNotes.Add(new MemberGeneralNote
        {
            NoteId = Guid.NewGuid(),
            MemberId = created.MemberId,
            CenterId = CenterA,
            NoteText = "حساسية دوائية",
            AuthorUserId = "actor-1",
            AuthorClinicId = ClinicEyes,
            IsActive = true,
            WrittenAt = DateTimeOffset.UtcNow,
            UpdatedByUserId = "actor-1",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var workspace = CreateWorkspace(db, CenterA, "actor-1");

        var summary = await workspace.OpenMemberAsync(created.MemberId, ClinicEyes);

        Assert.NotNull(summary.LatestGeneralNote);
        Assert.Equal("حساسية دوائية", summary.LatestGeneralNote!.NoteText);
    }

    [Fact]
    public async Task Open_other_center_member_is_not_found()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedWorkspaceAsync(db);
        var membersB = MemberServiceTests.CreateService(db, CenterB, allowCreate: true, allowView: true);
        var other = await membersB.CreateAsync(ValidCreate("X-CENTER"));
        var workspace = CreateWorkspace(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            workspace.OpenMemberAsync(other.MemberId, ClinicEyes));
    }

    [Fact]
    public async Task Member_list_filters_by_queue_number()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var first = await service.CreateAsync(ValidCreate("QN-1"));
        await service.CreateAsync(ValidCreate("QN-2", "منتسب ثانٍ"));

        var page = await service.ListAsync(new MemberListQuery { QueueNumber = first.QueueNumber });

        Assert.Equal(first.MemberId, Assert.Single(page.Items).MemberId);
    }

    private static ClinicWorkspaceService CreateWorkspace(AppDbContext db, Guid centerId, string userId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Members.View,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser(userId, userId, "127.0.0.1");

        var memberService = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);

        // MemberService يستخدم TestCurrentUser خاصاً به، بينما مساحة العيادة تحتاج مستخدماً منفصلاً لملكية القفل.
        // Open/Search يستدعيان MemberService بـ actor-1؛ إسناد العيادة يعتمد على userId لمساحة العمل.
        return new ClinicWorkspaceService(
            memberService,
            new ClinicWorkspaceRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            new AuditLogRepository(db),
            new TestUnitOfWork(db),
            gate.Object,
            currentUser,
            Microsoft.Extensions.Options.Options.Create(new ClinicWorkspaceOptions { EditLockTimeoutMinutes = 30 }));
    }

    private static async Task SeedWorkspaceAsync(AppDbContext db, bool assignDoctorToEyes = true)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedCenterClinicAsync(db, CenterA, ClinicEnt);
        await SeedCenterClinicAsync(db, CenterB, ClinicEyes);
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

        if (assignDoctorToEyes)
            await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEyes);
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

    private static async Task SeedCenterClinicAsync(AppDbContext db, Guid centerId, Guid clinicId, bool isActive = true)
    {
        if (await db.CenterClinics.AnyAsync(x => x.CenterId == centerId && x.ClinicId == clinicId))
            return;

        db.CenterClinics.Add(new CenterClinic
        {
            CenterClinicId = Guid.NewGuid(),
            CenterId = centerId,
            ClinicId = clinicId,
            IsActive = isActive,
            ActivatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedDoctorAssignmentAsync(AppDbContext db, string userId, Guid centerId, Guid clinicId)
    {
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

    private static async Task<Guid> SeedCurrentExamAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        short resultStatusId)
    {
        var examId = Guid.NewGuid();
        var assessmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ExaminerUserId = "actor-1",
            ExaminedAt = now,
            ExamSequence = 1,
            IsCurrent = true,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ClinicAssessments.Add(new ClinicAssessment
        {
            AssessmentId = assessmentId,
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = resultStatusId == 2 ? "سبب" : null,
            AssessedByUserId = "actor-1",
            AssessedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return assessmentId;
    }

    private static CreateMemberRequest ValidCreate(string recruitment, string fullName = "منتسب عيادة")
        => new()
        {
            FullName = fullName,
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
        };

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public TestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new NoopTransaction());
    }

    private sealed class NoopTransaction : IDbContextTransaction
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
