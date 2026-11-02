using Application.Common.Permissions;
using Application.Dtos.Archive;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Mappings;
using Application.Services;
using Application.Services.Archive;
using Application.Services.ClinicWorkspace;
using Application.Services.Supervisor;
using Domain.Entities.Archive;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class MemberArchiveServiceTests
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
    public async Task SearchLocal_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, allowLocal: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.SearchLocalAsync(new ArchiveSearchQuery()));
    }

    [Fact]
    public async Task SearchLocal_isolates_centers_and_finds_by_keys()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        var (archiveA, memberA) = await CreateArchivedMemberAsync(db, CenterA, "ARCH-A", 1);
        await CreateArchivedMemberAsync(db, CenterB, "ARCH-B", 1);

        var page = await CreateService(db, CenterA).SearchLocalAsync(new ArchiveSearchQuery
        {
            ArchiveNumber = archiveA.ArchiveNumber,
            FileNumber = memberA.FileNumber,
            FullName = "ARCH-A",
            RecruitmentNumber = "ARCH-A"
        });

        Assert.Equal(1, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal(archiveA.ArchiveId, item.ArchiveId);
        Assert.Equal(CenterA, item.CenterId);
        Assert.Equal(1, item.ResultStatusId);
        Assert.DoesNotContain(page.Items, i => i.CenterId == CenterB);
    }

    [Fact]
    public async Task SearchLocal_excludes_other_center_as_not_found_scope()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateArchivedMemberAsync(db, CenterB, "ONLY-B", 2, "رفض");

        var page = await CreateService(db, CenterA).SearchLocalAsync(new ArchiveSearchQuery
        {
            RecruitmentNumber = "ONLY-B"
        });

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task SearchCentral_requires_access_reason_and_records_cross_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateArchivedMemberAsync(db, CenterA, "CEN-A", 1);

        var cross = new Mock<ICrossCenterAccessService>();
        cross.Setup(c => c.RecordAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(db, CenterA, allowLocal: true, allowCentral: true, crossCenter: cross.Object);

        await Assert.ThrowsAsync<InvalidArchiveFilterException>(() =>
            service.SearchCentralAsync(new ArchiveSearchQuery(), "  "));

        var page = await service.SearchCentralAsync(new ArchiveSearchQuery(), "مراجعة مركزية");
        Assert.Equal(1, page.TotalCount);
        cross.Verify(c => c.RecordAsync(
            CenterA,
            "مراجعة مركزية",
            MemberArchiveService.CrossCenterDataType,
            It.IsAny<string?>(),
            nameof(MemberArchive),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchCentral_ignores_same_center_cross_access_rejection()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateArchivedMemberAsync(db, CenterA, "SAME-CEN", 1);

        var cross = new Mock<ICrossCenterAccessService>();
        cross.Setup(c => c.RecordAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidUserDataException("الاطلاع ضمن المركز النشط ليس اطلاعاً عابراً."));

        var page = await CreateService(db, CenterA, allowLocal: true, allowCentral: true, crossCenter: cross.Object)
            .SearchCentralAsync(new ArchiveSearchQuery(), "مراجعة مركزية");

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(CenterA, Assert.Single(page.Items).CenterId);
    }

    [Fact]
    public async Task GetFullReportLocal_returns_sections_without_storage_key()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var (archive, _) = await CreateArchivedMemberAsync(db, CenterA, "REP-A", 1);

        var report = await CreateService(db, CenterA).GetFullReportLocalAsync(archive.ArchiveId);

        Assert.True(report.IsComplete);
        Assert.Null(report.IncompleteReasonAr);
        Assert.Equal(archive.ArchiveNumber, report.ArchiveNumber);
        Assert.Equal(CenterA, report.ArchiveCenterId);
        Assert.Equal(5, report.ClinicAssessments.Count);
        Assert.All(report.ClinicAssessments, r => Assert.False(r.IsMissing));
        Assert.Equal(1, report.FinalDecision.ResultStatusId);
        Assert.DoesNotContain(
            System.Text.Json.JsonSerializer.Serialize(report),
            "StorageKey",
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(db.AuditEvents, e => e.EventType == MemberArchiveService.ArchiveReportViewEventType);
    }

    [Fact]
    public async Task GetFullReportLocal_other_center_is_not_found()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var (archive, _) = await CreateArchivedMemberAsync(db, CenterB, "REP-B", 1);

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            CreateService(db, CenterA).GetFullReportLocalAsync(archive.ArchiveId));
    }

    [Fact]
    public async Task GetFullReport_preserves_decision_history_on_amend_without_second_archive()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var (archive, member) = await CreateArchivedMemberAsync(db, CenterA, "HIST-A", 1);

        var decision = await db.FinalDecisions.SingleAsync(d => d.FinalDecisionId == archive.FinalDecisionId);
        var now = DateTimeOffset.UtcNow;
        db.FinalDecisionHistories.Add(new FinalDecisionHistory
        {
            HistoryId = Guid.NewGuid(),
            FinalDecisionId = decision.FinalDecisionId,
            MemberId = member.MemberId,
            CenterId = CenterA,
            OldStatusId = 1,
            NewStatusId = 2,
            ChangeReason = "تعديل نتيجة",
            ChangedByUserId = "actor-1",
            ChangedAt = now,
            ExportResetPerformed = true
        });
        decision.ResultStatusId = 2;
        decision.RejectionReason = "سبب رفض بعد التعديل";
        decision.UpdatedAt = now;
        archive.UpdatedAt = now;
        await db.SaveChangesAsync();

        var report = await CreateService(db, CenterA).GetFullReportLocalAsync(archive.ArchiveId);

        Assert.Equal(2, report.FinalDecision.ResultStatusId);
        Assert.Single(report.DecisionHistory);
        Assert.Equal("تعديل نتيجة", report.DecisionHistory[0].ChangeReason);
        Assert.Equal(1, await db.MemberArchives.CountAsync(a => a.MemberId == member.MemberId));
    }

    [Fact]
    public async Task PrintLocal_generates_arabic_pdf()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var (archive, _) = await CreateArchivedMemberAsync(db, CenterA, "PDF-A", 1);

        var file = await CreateService(db, CenterA).PrintFullReportLocalAsync(archive.ArchiveId);

        Assert.Equal("application/pdf", file.ContentType);
        Assert.NotEmpty(file.FileContents);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents.AsSpan(0, 4)));
        Assert.Contains(archive.ArchiveNumber, file.FileName);
        Assert.Contains(db.AuditEvents, e => e.EventType == MemberArchiveService.ArchiveReportPrintEventType);
    }

    [Fact]
    public async Task PrintLocal_rejects_unauthorized()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var (archive, _) = await CreateArchivedMemberAsync(db, CenterA, "PDF-DENY", 1);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            CreateService(db, CenterA, allowLocal: false).PrintFullReportLocalAsync(archive.ArchiveId));
    }

    private static MemberArchiveService CreateService(
        AppDbContext db,
        Guid centerId,
        bool allowLocal = true,
        bool allowCentral = true,
        ICrossCenterAccessService? crossCenter = null)
    {
        var gate = new Mock<IPermissionGate>();
        if (allowLocal)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Archive.ViewPrint,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(centerId);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Archive.ViewPrint,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        if (allowCentral)
        {
            gate.Setup(g => g.EnsureCentralAsync(
                    MilitaryHealthPermissions.Archive.ViewPrint,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        else
        {
            gate.Setup(g => g.EnsureCentralAsync(
                    MilitaryHealthPermissions.Archive.ViewPrint,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        gate.Setup(g => g.Has(MilitaryHealthPermissions.Medical.AttachmentsRead)).Returns(true);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-1", "actor", "127.0.0.1");

        var config = new TypeAdapterConfig();
        new MemberProfile().Register(config);
        var mapper = new Mapper(config);

        var medicalRequests = new MemberMedicalRequestService(
            new ConsultationRepository(db),
            new InvestigationRepository(db),
            new AttachmentRepository(db),
            new MemberRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            gate.Object);

        return new MemberArchiveService(
            gate.Object,
            currentUser,
            new MemberArchiveRepository(db),
            new MemberRepository(db),
            new ClinicWorkspaceRepository(db),
            new CenterClinicRepository(db),
            new ReferenceDataRepository(db),
            new MedicalCenterRepository(db),
            new UserRepository(db),
            medicalRequests,
            crossCenter ?? Mock.Of<ICrossCenterAccessService>(),
            new AuditLogRepository(db),
            new ArchiveTestUnitOfWork(db),
            mapper);
    }

    private static async Task<(MemberArchive Archive, Domain.Entities.Patient.Member Member)> CreateArchivedMemberAsync(
        AppDbContext db,
        Guid centerId,
        string recruitment,
        short resultStatusId,
        string? rejectionReason = null)
    {
        var members = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = $"منتسب {recruitment}",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-22),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = recruitment,
            RecruitmentCenterId = RecruitmentCenterId,
            BloodTypeId = 1,
            HeightCm = 170,
            WeightKg = 70,
            BloodPressure = "120/80",
            PulseBpm = 72,
            HasTattoo = false
        });

        foreach (var clinicId in AllClinics)
            await SeedAssessmentAsync(db, created.MemberId, centerId, clinicId, resultStatusId == 3 ? (short)1 : resultStatusId);

        var now = DateTimeOffset.UtcNow;
        var decisionId = Guid.NewGuid();
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = decisionId,
            MemberId = created.MemberId,
            CenterId = centerId,
            SupervisorUserId = "actor-1",
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });

        var center = await db.MedicalCenters.SingleAsync(c => c.CenterId == centerId);
        var seq = await db.MemberArchives.CountAsync(a => a.CenterId == centerId) + 1;
        var archive = new MemberArchive
        {
            ArchiveId = Guid.NewGuid(),
            MemberId = created.MemberId,
            FinalDecisionId = decisionId,
            CenterId = centerId,
            ArchiveNumber = $"{center.Code}-{seq:D8}",
            ArchivedAt = now,
            UpdatedAt = now
        };
        db.MemberArchives.Add(archive);
        await db.SaveChangesAsync();

        var member = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        return (archive, member);
    }

    private static async Task SeedAssessmentAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        short resultStatusId)
    {
        var examId = Guid.NewGuid();
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
            AssessmentId = Guid.NewGuid(),
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = resultStatusId == 2 ? "سبب" : null,
            ClinicalNotes = "ملاحظة",
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
        foreach (var (id, code, name, sort) in new (Guid, string, string, int)[]
                 {
                     (ClinicEyes, "EYE", "العيون", 1),
                     (ClinicEnt, "ENT", "الأذنية", 2),
                     (ClinicInternal, "INTERNAL", "الباطنة", 3),
                     (ClinicSurgery, "SURGERY", "الجراحة", 4),
                     (ClinicOrtho, "ORTHOPEDIC", "العظمية", 5)
                 })
        {
            if (!await db.Clinics.AnyAsync(c => c.ClinicId == id))
            {
                db.Clinics.Add(new Clinic
                {
                    ClinicId = id,
                    Code = code,
                    NameAr = name,
                    SortOrder = (short)sort,
                    CountsForFinal = true,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync();
        foreach (var clinicId in AllClinics)
        {
            foreach (var centerId in new[] { CenterA, CenterB })
            {
                if (!await db.CenterClinics.AnyAsync(x => x.CenterId == centerId && x.ClinicId == clinicId))
                {
                    db.CenterClinics.Add(new CenterClinic
                    {
                        CenterClinicId = Guid.NewGuid(),
                        CenterId = centerId,
                        ClinicId = clinicId,
                        IsActive = true,
                        ActivatedAt = DateTimeOffset.UtcNow
                    });
                }
            }
        }

        if (!await db.ResultStatuses.AnyAsync())
        {
            db.ResultStatuses.AddRange(
                new ResultStatus { StatusId = 1, Code = "ACCEPTED", NameAr = "مقبول", IsActive = true },
                new ResultStatus { StatusId = 2, Code = "REJECTED", NameAr = "مرفوض", IsActive = true },
                new ResultStatus { StatusId = 3, Code = "DEFERRED", NameAr = "مؤجل", IsActive = true });
        }

        if (!await db.RequiredActionTypes.AnyAsync(t => t.ActionTypeId == 1))
        {
            db.RequiredActionTypes.Add(new RequiredActionType
            {
                ActionTypeId = 1,
                Code = "FORM",
                NameAr = "طلب استمارة",
                RequiresDescription = false,
                RequiresTargetClinic = false,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed class ArchiveTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public ArchiveTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new NoopTx());
    }

    private sealed class NoopTx : IDbContextTransaction
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
