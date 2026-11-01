using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Dtos.Supervisor;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.Supervisor;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class FinalDecisionServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClinicInternal = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ClinicSurgery = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ClinicOrtho = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly Guid[] AllClinics =
    [
        ClinicEyes, ClinicEnt, ClinicInternal, ClinicSurgery, ClinicOrtho
    ];

    [Fact]
    public async Task Issue_accepted_compatible_with_all_clinics_succeeds()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-OK");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        var result = await service.IssueAsync(memberId, new IssueFinalDecisionRequest
        {
            ResultStatusId = 1,
            ExpectedDataRevision = 1
        });

        Assert.Equal(memberId, result.MemberId);
        Assert.Equal(CenterA, result.CenterId);
        Assert.Equal((short)1, result.ResultStatusId);
        Assert.Equal("مقبول", result.ResultStatusNameAr);
        Assert.Null(result.RejectionReason);
        Assert.Null(result.DisagreementJustification);
        Assert.Empty(result.DisagreeingClinicCodes);
        Assert.Equal(0, result.ObjectionCount);
        Assert.False(result.ArchiveCreated);
        Assert.Equal(2, result.MemberDataRevision);
        Assert.Equal("actor-1", result.SupervisorUserId);

        Assert.Equal(1, await db.FinalDecisions.CountAsync());
        Assert.Equal(0, await db.SupervisorObjections.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == FinalDecisionService.FinalDecisionIssuedEventType
            && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Issue_rejected_requires_reason_and_persists()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-REJ");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 2);

        var service = CreateService(db, CenterA, allow: true);

        await Assert.ThrowsAsync<InvalidFinalDecisionDataException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 2 }));

        var result = await service.IssueAsync(memberId, new IssueFinalDecisionRequest
        {
            ResultStatusId = 2,
            RejectionReason = "غير لائق طبياً"
        });

        Assert.Equal((short)2, result.ResultStatusId);
        Assert.Equal("غير لائق طبياً", result.RejectionReason);
        Assert.Equal("مرفوض", result.ResultStatusNameAr);
    }

    [Fact]
    public async Task Issue_disagreement_without_justification_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DIS");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        var error = await Assert.ThrowsAsync<FinalDecisionDisagreementJustificationRequiredException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest
            {
                ResultStatusId = 2,
                RejectionReason = "رفض مشرف"
            }));

        Assert.Equal("FINAL_DECISION_DISAGREEMENT_JUSTIFICATION_REQUIRED", error.ErrorCode);
        Assert.Equal(0, await db.FinalDecisions.CountAsync());
        Assert.Equal(0, await db.SupervisorObjections.CountAsync());
    }

    [Fact]
    public async Task Issue_disagreement_with_justification_creates_objections()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-OBJ");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        var result = await service.IssueAsync(memberId, new IssueFinalDecisionRequest
        {
            ResultStatusId = 2,
            RejectionReason = "رفض بعد مراجعة شاملة",
            DisagreementJustification = "تعارض مع تقدير العيادات ويُعتمد الرفض"
        });

        Assert.Equal(5, result.ObjectionCount);
        Assert.Equal(5, result.DisagreeingClinicCodes.Count);
        Assert.Contains("EYE", result.DisagreeingClinicCodes);
        Assert.Equal("تعارض مع تقدير العيادات ويُعتمد الرفض", result.DisagreementJustification);

        var objections = await db.SupervisorObjections.ToListAsync();
        Assert.Equal(5, objections.Count);
        Assert.All(objections, o =>
        {
            Assert.Equal(FinalDecisionService.TargetScopeOneClinic, o.TargetScope);
            Assert.NotNull(o.ClinicAssessmentId);
            Assert.Equal(result.FinalDecisionId, o.FinalDecisionId);
            Assert.Equal(FinalDecisionService.ObjectionStatusOpen, o.Status);
        });
    }

    [Fact]
    public async Task Issue_incomplete_clinics_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-INC");
        await SeedAssessmentAsync(db, memberId, CenterA, ClinicEyes, 1);
        await SeedAssessmentAsync(db, memberId, CenterA, ClinicEnt, 1);

        var service = CreateService(db, CenterA, allow: true);
        var error = await Assert.ThrowsAsync<IncompleteClinicAssessmentsException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 }));

        Assert.Equal("FINAL_DECISION_INCOMPLETE_CLINICS", error.ErrorCode);
        Assert.Equal(0, await db.FinalDecisions.CountAsync());
    }

    [Fact]
    public async Task Issue_second_decision_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DUP");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        await service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 });

        await Assert.ThrowsAsync<FinalDecisionAlreadyExistsException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 }));

        Assert.Equal(1, await db.FinalDecisions.CountAsync());
    }

    [Fact]
    public async Task Issue_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-PERM");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: false);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 }));
    }

    [Fact]
    public async Task Issue_rejects_member_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-XCTR");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var serviceB = CreateService(db, CenterB, allow: true);
        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            serviceB.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 1 }));
    }

    [Fact]
    public async Task Issue_rejects_stale_data_revision()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-REV");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        await Assert.ThrowsAsync<FinalDecisionConcurrencyException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest
            {
                ResultStatusId = 1,
                ExpectedDataRevision = 99
            }));

        Assert.Equal(0, await db.FinalDecisions.CountAsync());
    }

    [Fact]
    public async Task Issue_rejects_deferred_result_in_this_card()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-DEF");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 1);

        var service = CreateService(db, CenterA, allow: true);
        await Assert.ThrowsAsync<InvalidFinalDecisionDataException>(() =>
            service.IssueAsync(memberId, new IssueFinalDecisionRequest { ResultStatusId = 3 }));
    }

    [Fact]
    public async Task Issue_compatible_reject_does_not_require_justification()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FD-COMP");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, resultStatusId: 2);

        var service = CreateService(db, CenterA, allow: true);
        var result = await service.IssueAsync(memberId, new IssueFinalDecisionRequest
        {
            ResultStatusId = 2,
            RejectionReason = "موافق لجميع العيادات"
        });

        Assert.Equal(0, result.ObjectionCount);
        Assert.Null(result.DisagreementJustification);
    }

    #region Helpers

    private static FinalDecisionService CreateService(AppDbContext db, Guid centerId, bool allow)
    {
        var gate = new Mock<IPermissionGate>();
        if (allow)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Clinical.FinalDecision,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(centerId);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Clinical.FinalDecision,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-1", "actor", "127.0.0.1");

        return new FinalDecisionService(
            gate.Object,
            currentUser,
            new MemberRepository(db),
            new ClinicWorkspaceRepository(db),
            new CenterClinicRepository(db),
            new ReferenceDataRepository(db),
            new AuditLogRepository(db),
            new FinalDecisionTestUnitOfWork(db));
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, Guid centerId, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب قرار نهائي",
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

    private static async Task SeedAllAssessmentsAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        short resultStatusId)
    {
        foreach (var clinicId in AllClinics)
        {
            await SeedAssessmentAsync(
                db,
                memberId,
                centerId,
                clinicId,
                resultStatusId,
                rejectionReason: resultStatusId == 2 ? "سبب عيادة" : null);
        }
    }

    private static async Task SeedAssessmentAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        short resultStatusId,
        string? rejectionReason = null)
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
            RejectionReason = rejectionReason,
            ClinicalNotes = "ملاحظات",
            AssessedByUserId = "actor-1",
            AssessedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedClinicAsync(db, ClinicInternal, "INTERNAL", "الباطنة", 3);
        await SeedClinicAsync(db, ClinicSurgery, "SURGERY", "الجراحة", 4);
        await SeedClinicAsync(db, ClinicOrtho, "ORTHOPEDIC", "العظمية", 5);

        foreach (var clinicId in AllClinics)
        {
            await SeedCenterClinicAsync(db, CenterA, clinicId);
            await SeedCenterClinicAsync(db, CenterB, clinicId);
        }

        if (!await db.ResultStatuses.AnyAsync())
        {
            db.ResultStatuses.AddRange(
                new ResultStatus { StatusId = 1, Code = "ACCEPTED", NameAr = "مقبول", IsActive = true },
                new ResultStatus { StatusId = 2, Code = "REJECTED", NameAr = "مرفوض", IsActive = true },
                new ResultStatus { StatusId = 3, Code = "DEFERRED", NameAr = "مؤجل", IsActive = true });
        }

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
            CountsForFinal = true,
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

    private sealed class FinalDecisionTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public FinalDecisionTestUnitOfWork(AppDbContext db) => _db = db;
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

    #endregion
}
