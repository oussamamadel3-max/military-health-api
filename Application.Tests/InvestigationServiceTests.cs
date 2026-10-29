using Application.Common.Permissions;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.ClinicWorkspace;
using Application.Services.Sanitization;
using Domain.Entities.Auth;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class InvestigationServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Create_lab_pending_links_member_doctor_clinic_center_and_audits()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "INV-1");
        var service = CreateService(db, CenterA, "actor-1");

        var created = await service.CreateAsync(memberId, ValidCreate(typeId: 1, testName: "CBC"));

        Assert.Equal(memberId, created.MemberId);
        Assert.Equal(CenterA, created.CenterId);
        Assert.Equal(ClinicEyes, created.ClinicId);
        Assert.Equal("actor-1", created.RequestedByUserId);
        Assert.Equal(InvestigationService.StatusPending, created.Status);
        Assert.Equal("CBC", created.TestName);
        Assert.Null(created.ResultText);
        Assert.Null(created.ResultedAt);

        Assert.Equal(1, await db.Investigations.CountAsync());
        var audit = Assert.Single(await db.AuditEvents.Where(e =>
            e.EventType == InvestigationService.InvestigationCreatedEventType).ToListAsync());
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal(nameof(Investigation), audit.EntityType);
    }

    [Fact]
    public async Task Create_radiology_with_result_marks_completed()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "INV-RAD");
        var service = CreateService(db, CenterA, "actor-1");

        var created = await service.CreateAsync(
            memberId,
            ValidCreate(typeId: 2, testName: "Chest X-Ray", resultText: "طبيعي"));

        Assert.Equal(InvestigationService.StatusCompleted, created.Status);
        Assert.Equal(2, created.InvestigationTypeId);
        Assert.Equal("طبيعي", created.ResultText);
        Assert.NotNull(created.ResultedAt);
    }

    [Fact]
    public async Task Create_allows_multiple_investigations_for_same_member()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "INV-MULTI");
        var service = CreateService(db, CenterA, "actor-1");

        await service.CreateAsync(memberId, ValidCreate(testName: "A"));
        await service.CreateAsync(memberId, ValidCreate(testName: "B"));

        Assert.Equal(2, await db.Investigations.CountAsync(i => i.MemberId == memberId));
    }

    [Fact]
    public async Task Update_result_completes_pending_without_closing_deferred()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "INV-DEF");
        var deferredId = await SeedDeferredAsync(db, memberId, CenterA, ClinicEyes);
        var service = CreateService(db, CenterA, "actor-1");

        var created = await service.CreateAsync(
            memberId,
            ValidCreate(deferredActionId: deferredId));

        Assert.Equal(deferredId, created.DeferredActionId);
        Assert.Equal(ClinicWorkspaceService.DeferredStatusRequested, created.DeferredActionStatus);

        var updated = await service.UpdateResultAsync(
            created.InvestigationId,
            new UpdateInvestigationResultRequest { ResultText = "نتيجة لاحقة" });

        Assert.Equal(InvestigationService.StatusCompleted, updated.Status);
        Assert.NotNull(updated.ResultedAt);

        var deferred = await db.DeferredActions.SingleAsync(d => d.DeferredActionId == deferredId);
        Assert.Equal(ClinicWorkspaceService.DeferredStatusRequested, deferred.Status);
        Assert.Null(deferred.CompletedAt);
        Assert.Null(deferred.CompletedByUserId);

        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == InvestigationService.InvestigationResultUpdatedEventType));
    }

    [Fact]
    public async Task List_pending_returns_only_pending_in_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "INV-PEND");
        var service = CreateService(db, CenterA, "actor-1");

        await service.CreateAsync(memberId, ValidCreate(testName: "Pending"));
        await service.CreateAsync(memberId, ValidCreate(testName: "Done", resultText: "ok"));

        var pending = await service.ListPendingAsync(ClinicEyes);

        Assert.Single(pending);
        Assert.Equal("Pending", pending[0].TestName);
        Assert.Equal(InvestigationService.StatusPending, pending[0].Status);
    }

    [Fact]
    public async Task Create_rejects_missing_request_reason()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "INV-MISS");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<InvalidInvestigationDataException>(() =>
            service.CreateAsync(memberId, ValidCreate(reason: "  ")));
    }

    [Fact]
    public async Task Create_rejects_unassigned_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db, assignDoctorToEyes: false);
        var memberId = await CreateMemberAsync(db, CenterA, "INV-UN");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<DoctorClinicNotAssignedException>(() =>
            service.CreateAsync(memberId, ValidCreate()));
    }

    [Fact]
    public async Task Create_rejects_member_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var otherMemberId = await CreateMemberAsync(db, CenterB, "INV-OTHER");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            service.CreateAsync(otherMemberId, ValidCreate()));
    }

    [Fact]
    public async Task Create_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "INV-PERM");
        var service = CreateService(db, CenterA, "actor-1", allowInvestigations: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.CreateAsync(memberId, ValidCreate()));
    }

    [Fact]
    public async Task Create_rejects_deferred_from_other_member()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberA = await CreateMemberAsync(db, CenterA, "INV-A");
        var memberB = await CreateMemberAsync(db, CenterA, "INV-B");
        var deferredB = await SeedDeferredAsync(db, memberB, CenterA, ClinicEyes);
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<InvalidInvestigationDataException>(() =>
            service.CreateAsync(memberA, ValidCreate(deferredActionId: deferredB)));
    }

    [Fact]
    public async Task Get_rejects_investigation_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "INV-X");
        var serviceA = CreateService(db, CenterA, "actor-1");
        var created = await serviceA.CreateAsync(memberId, ValidCreate());

        await SeedDoctorAssignmentAsync(db, "actor-1", CenterB, ClinicEyes);
        var serviceB = CreateService(db, CenterB, "actor-1");

        await Assert.ThrowsAsync<InvestigationNotFoundException>(() =>
            serviceB.GetByIdAsync(created.InvestigationId));
    }

    private static CreateInvestigationRequest ValidCreate(
        short typeId = 1,
        string testName = "فحص مخبري",
        string reason = "متابعة طبية",
        string? resultText = null,
        Guid? deferredActionId = null)
        => new()
        {
            ClinicId = ClinicEyes,
            InvestigationTypeId = typeId,
            TestName = testName,
            RequestReason = reason,
            ResultText = resultText,
            DeferredActionId = deferredActionId
        };

    private static InvestigationService CreateService(
        AppDbContext db,
        Guid centerId,
        string userId,
        bool allowInvestigations = true)
    {
        var gate = new Mock<IPermissionGate>();
        if (allowInvestigations)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(centerId);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        var currentUser = new TestCurrentUser();
        currentUser.SetUser(userId, userId, "127.0.0.1");

        return new InvestigationService(
            new InvestigationRepository(db),
            new ClinicWorkspaceRepository(db),
            new MemberRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            new AuditLogRepository(db),
            new TestUnitOfWork(db),
            gate.Object,
            currentUser,
            new HtmlTextInputSanitizer());
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, Guid centerId, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب استقصاء",
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

    private static async Task<Guid> SeedDeferredAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId)
    {
        var id = Guid.NewGuid();
        db.DeferredActions.Add(new DeferredAction
        {
            DeferredActionId = id,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            DeferReason = "يحتاج تحليل",
            ActionTypeId = 1,
            ActionDescription = "طلب تحليل",
            FollowupNote = "متابعة",
            Status = ClinicWorkspaceService.DeferredStatusRequested,
            RequestedByUserId = "actor-1",
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedAsync(AppDbContext db, bool assignDoctorToEyes = true)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedCenterClinicAsync(db, CenterA, ClinicEnt);
        await SeedCenterClinicAsync(db, CenterB, ClinicEyes);

        db.RequiredActionTypes.Add(new RequiredActionType
        {
            ActionTypeId = 1,
            Code = "LAB",
            NameAr = "طلب تحليل",
            RequiresDescription = true,
            RequiresTargetClinic = false,
            IsActive = true
        });
        db.InvestigationTypes.AddRange(
            new InvestigationType
            {
                InvestigationTypeId = 1,
                Code = "LAB",
                NameAr = "مخبري",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new InvestigationType
            {
                InvestigationTypeId = 2,
                Code = "RAD",
                NameAr = "شعاعي",
                SortOrder = 2,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
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
