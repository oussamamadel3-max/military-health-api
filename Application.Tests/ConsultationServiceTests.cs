using Application.Common.Permissions;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.ClinicWorkspace;
using Application.Services.Sanitization;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class ConsultationServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Create_pending_links_member_doctor_clinic_center_and_audits()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-1");
        var service = CreateService(db, CenterA, "actor-1");

        var created = await service.CreateAsync(memberId, ValidCreate());

        Assert.Equal(memberId, created.MemberId);
        Assert.Equal(CenterA, created.CenterId);
        Assert.Equal(ClinicEyes, created.ClinicId);
        Assert.Equal("actor-1", created.RequestedByUserId);
        Assert.Equal(ConsultationService.StatusPending, created.Status);
        Assert.Null(created.ResultText);
        Assert.Null(created.ResultedAt);
        Assert.Equal("إحالة لتخصص قلبي", created.ReferralReason);

        var row = Assert.Single(await db.Consultations.ToListAsync());
        Assert.Equal(created.ConsultationId, row.ConsultationId);
        Assert.Equal(CenterA, row.CenterId);
        Assert.Equal(ClinicEyes, row.ClinicId);

        var audit = Assert.Single(await db.AuditEvents.Where(e =>
            e.EventType == ConsultationService.ConsultationCreatedEventType).ToListAsync());
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal(nameof(Consultation), audit.EntityType);
        Assert.Equal(created.ConsultationId.ToString(), audit.EntityId);
        Assert.Equal(CenterA, audit.CenterId);
    }

    [Fact]
    public async Task Create_with_result_marks_completed()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-2");
        var service = CreateService(db, CenterA, "actor-1");

        var created = await service.CreateAsync(memberId, ValidCreate(resultText: "نتيجة فورية"));

        Assert.Equal(ConsultationService.StatusCompleted, created.Status);
        Assert.Equal("نتيجة فورية", created.ResultText);
        Assert.NotNull(created.ResultedAt);
    }

    [Fact]
    public async Task Create_allows_multiple_consultations_for_same_member()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-MULTI");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.CreateAsync(memberId, ValidCreate(reason: "سبب أول"));
        var second = await service.CreateAsync(memberId, ValidCreate(reason: "سبب ثانٍ"));

        Assert.NotEqual(first.ConsultationId, second.ConsultationId);
        Assert.Equal(2, await db.Consultations.CountAsync(c => c.MemberId == memberId));
    }

    [Fact]
    public async Task Update_result_completes_pending_and_audits()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-RES");
        var service = CreateService(db, CenterA, "actor-1");
        var created = await service.CreateAsync(memberId, ValidCreate());

        var updated = await service.UpdateResultAsync(
            created.ConsultationId,
            new UpdateConsultationResultRequest
            {
                ResultText = "نتيجة لاحقة",
                ConsultantName = "د. استشاري"
            });

        Assert.Equal(ConsultationService.StatusCompleted, updated.Status);
        Assert.Equal("نتيجة لاحقة", updated.ResultText);
        Assert.Equal("د. استشاري", updated.ConsultantName);
        Assert.NotNull(updated.ResultedAt);

        var audit = Assert.Single(await db.AuditEvents.Where(e =>
            e.EventType == ConsultationService.ConsultationResultUpdatedEventType).ToListAsync());
        Assert.Equal("SUCCESS", audit.Result);
        Assert.False(string.IsNullOrWhiteSpace(audit.OldValues));
        Assert.False(string.IsNullOrWhiteSpace(audit.NewValues));
    }

    [Fact]
    public async Task Create_rejects_missing_referral_reason()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-MISS");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<InvalidConsultationDataException>(() =>
            service.CreateAsync(memberId, ValidCreate(reason: "   ")));
    }

    [Fact]
    public async Task Create_rejects_inactive_consultation_type()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        db.ConsultationTypes.Add(new ConsultationType
        {
            ConsultationTypeId = 2,
            Code = "INACTIVE",
            NameAr = "غير فعال",
            SortOrder = 2,
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-TYPE");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<InvalidConsultationDataException>(() =>
            service.CreateAsync(memberId, ValidCreate(typeId: 2)));
    }

    [Fact]
    public async Task Create_rejects_unassigned_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db, assignDoctorToEyes: false);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-UNASSIGNED");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<DoctorClinicNotAssignedException>(() =>
            service.CreateAsync(memberId, ValidCreate()));
    }

    [Fact]
    public async Task Create_rejects_member_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var otherMemberId = await CreateMemberAsync(db, CenterB, "CONS-OTHER");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            service.CreateAsync(otherMemberId, ValidCreate()));
    }

    [Fact]
    public async Task Create_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-PERM");
        var service = CreateService(db, CenterA, "actor-1", allowConsultations: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.CreateAsync(memberId, ValidCreate()));
    }

    [Fact]
    public async Task Update_result_rejects_completed_consultation()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-DONE");
        var service = CreateService(db, CenterA, "actor-1");
        var created = await service.CreateAsync(memberId, ValidCreate(resultText: "جاهزة"));

        await Assert.ThrowsAsync<InvalidConsultationStateException>(() =>
            service.UpdateResultAsync(
                created.ConsultationId,
                new UpdateConsultationResultRequest { ResultText = "تعديل" }));
    }

    [Fact]
    public async Task Get_rejects_consultation_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-XGET");
        var serviceA = CreateService(db, CenterA, "actor-1");
        var created = await serviceA.CreateAsync(memberId, ValidCreate());

        await SeedDoctorAssignmentAsync(db, "actor-1", CenterB, ClinicEyes);
        var serviceB = CreateService(db, CenterB, "actor-1");

        await Assert.ThrowsAsync<ConsultationNotFoundException>(() =>
            serviceB.GetByIdAsync(created.ConsultationId));
    }

    [Fact]
    public async Task List_returns_only_assigned_clinic_rows_for_member()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEnt);
        var memberId = await CreateMemberAsync(db, CenterA, "CONS-LIST");
        var service = CreateService(db, CenterA, "actor-1");

        await service.CreateAsync(memberId, ValidCreate(reason: "عيون"));
        await service.CreateAsync(memberId, new CreateConsultationRequest
        {
            ClinicId = ClinicEnt,
            ConsultationTypeId = 1,
            ReferralReason = "أذنية"
        });

        var eyesOnly = await service.ListByMemberAsync(memberId, ClinicEyes);
        Assert.Single(eyesOnly);
        Assert.All(eyesOnly, x => Assert.Equal(ClinicEyes, x.ClinicId));

        var allAssigned = await service.ListByMemberAsync(memberId);
        Assert.Equal(2, allAssigned.Count);
    }

    private static CreateConsultationRequest ValidCreate(
        string reason = "إحالة لتخصص قلبي",
        string? resultText = null,
        short typeId = 1)
        => new()
        {
            ClinicId = ClinicEyes,
            ConsultationTypeId = typeId,
            ReferralReason = reason,
            ResultText = resultText
        };

    private static ConsultationService CreateService(
        AppDbContext db,
        Guid centerId,
        string userId,
        bool allowConsultations = true)
    {
        var gate = new Mock<IPermissionGate>();
        if (allowConsultations)
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

        return new ConsultationService(
            new ConsultationRepository(db),
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
            FullName = "منتسب استشارة",
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

    private static async Task SeedAsync(AppDbContext db, bool assignDoctorToEyes = true)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedCenterClinicAsync(db, CenterA, ClinicEnt);
        await SeedCenterClinicAsync(db, CenterB, ClinicEyes);

        db.ConsultationTypes.Add(new ConsultationType
        {
            ConsultationTypeId = 1,
            Code = "CARDIO",
            NameAr = "قلبية",
            SortOrder = 1,
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
