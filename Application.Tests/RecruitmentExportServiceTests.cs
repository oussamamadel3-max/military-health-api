using Application.Common.Permissions;
using Application.Dtos.Export;
using Application.Dtos.Members;
using Application.Dtos.Supervisor;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services;
using Application.Services.Export;
using Application.Services.Supervisor;
using Domain.Entities.Audit;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class RecruitmentExportServiceTests
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
    public async Task ListPending_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, allowRecruitment: false, allowHistory: true);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ListPendingAsync(new RecruitmentExportPendingQuery()));
    }

    [Fact]
    public async Task ListPending_excludes_unreviewed_current_revision()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var reviewed = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-REV", 1);
        var unreviewed = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-UNREV", 1);
        var member = await db.Members.SingleAsync(m => m.MemberId == unreviewed);
        member.ReviewedRevision = null;
        member.ReviewedAt = null;
        member.ReviewedByUserId = null;
        await db.SaveChangesAsync();

        var page = await CreateService(db, CenterA).ListPendingAsync(new RecruitmentExportPendingQuery());

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(reviewed, page.Items.Single().MemberId);
    }

    [Fact]
    public async Task ListPending_includes_member_after_confirm_current_revision()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-CONFIRM", 1);
        var member = await db.Members.SingleAsync(m => m.MemberId == memberId);
        member.ReviewedRevision = null;
        member.ReviewedAt = null;
        member.ReviewedByUserId = null;
        member.DataRevision = 74;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var before = await CreateService(db, CenterA).ListPendingAsync(new RecruitmentExportPendingQuery());
        Assert.Equal(0, before.TotalCount);

        var decisions = CreateFinalDecisionService(db, CenterA);
        var confirmed = await decisions.ConfirmCurrentRevisionAsync(
            memberId,
            new ConfirmCurrentRevisionRequest { ExpectedDataRevision = 74 });

        Assert.True(confirmed.EligibleForRecruitmentExport);

        var after = await CreateService(db, CenterA).ListPendingAsync(new RecruitmentExportPendingQuery());
        Assert.Equal(1, after.TotalCount);
        Assert.Equal(memberId, after.Items.Single().MemberId);
    }

    [Fact]
    public async Task CreateBatch_excludes_unreviewed_current_revision()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var reviewed = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-OK", 1);
        var unreviewed = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-NEED-REV", 1);
        var member = await db.Members.SingleAsync(m => m.MemberId == unreviewed);
        member.ReviewedRevision = null;
        member.ReviewedAt = null;
        member.ReviewedByUserId = null;
        await db.SaveChangesAsync();

        var result = await CreateService(db, CenterA).CreateBatchAsync(
            new CreateRecruitmentExportBatchRequest { MemberIds = [reviewed, unreviewed] });

        Assert.Equal(1, result.RecordCount);
        Assert.Contains(result.Exclusions, e => e.MemberId == unreviewed && e.ReasonAr.Contains("مُراجَعة"));
        Assert.Equal(
            FinalDecisionService.ExportStatusExported,
            (await db.Members.SingleAsync(m => m.MemberId == reviewed)).ExportStatus);
        Assert.Equal(
            FinalDecisionService.ExportStatusUnexported,
            (await db.Members.SingleAsync(m => m.MemberId == unreviewed)).ExportStatus);
    }

    [Fact]
    public async Task ListPending_includes_accept_reject_unexported_only()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        var accepted = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-ACC", 1);
        var rejected = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-REJ", 2, rejectionReason: "سبب رفض");
        var deferred = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-DEF", 3, rejectionReason: "مؤجل");
        var noDecision = await CreateMemberAsync(db, CenterA, "EXP-NONE");
        var otherCenter = await CreateMemberWithDecisionAsync(db, CenterB, "EXP-B", 1);
        var already = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-DONE", 1);
        var done = await db.Members.SingleAsync(m => m.MemberId == already);
        done.ExportStatus = FinalDecisionService.ExportStatusExported;
        done.LastExportedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var page = await CreateService(db, CenterA).ListPendingAsync(new RecruitmentExportPendingQuery());

        Assert.Equal(2, page.TotalCount);
        var ids = page.Items.Select(i => i.MemberId).ToHashSet();
        Assert.Contains(accepted, ids);
        Assert.Contains(rejected, ids);
        Assert.DoesNotContain(deferred, ids);
        Assert.DoesNotContain(noDecision, ids);
        Assert.DoesNotContain(otherCenter, ids);
        Assert.DoesNotContain(already, ids);
    }

    [Fact]
    public async Task ListPending_isolates_centers()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberWithDecisionAsync(db, CenterA, "EXP-A1", 1);
        await CreateMemberWithDecisionAsync(db, CenterB, "EXP-B1", 1);

        var pageA = await CreateService(db, CenterA).ListPendingAsync(new RecruitmentExportPendingQuery());
        var pageB = await CreateService(db, CenterB).ListPendingAsync(new RecruitmentExportPendingQuery());

        Assert.Equal(1, pageA.TotalCount);
        Assert.Equal(1, pageB.TotalCount);
        Assert.Equal("EXP-A1", pageA.Items.Single().RecruitmentNumber);
        Assert.Equal("EXP-B1", pageB.Items.Single().RecruitmentNumber);
    }

    [Fact]
    public async Task CreateBatch_single_member_marks_exported_and_stores_file()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-ONE", 1);
        var storage = CreateStorageMock(out var savedPaths);

        var result = await CreateService(db, CenterA, fileStorage: storage.Object)
            .CreateBatchAsync(new CreateRecruitmentExportBatchRequest
            {
                MemberIds = [memberId]
            });

        Assert.Equal(RecruitmentExportService.StatusCompleted, result.Status);
        Assert.Equal(1, result.RecordCount);
        Assert.NotNull(result.GeneratedFileId);
        Assert.NotNull(result.DownloadPath);
        Assert.Single(savedPaths);

        var member = await db.Members.SingleAsync(m => m.MemberId == memberId);
        Assert.Equal(FinalDecisionService.ExportStatusExported, member.ExportStatus);
        Assert.NotNull(member.LastExportedAt);
        Assert.Equal(member.DataRevision, member.LastExportedRevision);

        var batch = await db.RecruitmentExportBatches.SingleAsync();
        Assert.Equal(RecruitmentExportService.StatusCompleted, batch.Status);
        Assert.Equal(result.GeneratedFileId, batch.GeneratedFileId);

        var item = await db.RecruitmentExportItems.SingleAsync();
        Assert.Equal(memberId, item.MemberId);
        Assert.False(item.IsReexport);
        Assert.Null(item.PreviousExportBatchId);

        Assert.Contains(await db.AuditEvents.ToListAsync(),
            e => e.EventType == RecruitmentExportService.BatchCompletedEventType);
    }

    [Fact]
    public async Task CreateBatch_export_all_pending_exports_multiple()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var a = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-M1", 1);
        var b = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-M2", 2, rejectionReason: "رفض");
        await CreateMemberWithDecisionAsync(db, CenterA, "EXP-M3", 3, rejectionReason: "مؤجل");

        var result = await CreateService(db, CenterA).CreateBatchAsync(
            new CreateRecruitmentExportBatchRequest { ExportAllPending = true });

        Assert.Equal(2, result.RecordCount);
        var exported = await db.Members
            .Where(m => m.ExportStatus == FinalDecisionService.ExportStatusExported)
            .Select(m => m.MemberId)
            .ToListAsync();
        Assert.Equal(2, exported.Count);
        Assert.Contains(a, exported);
        Assert.Contains(b, exported);
    }

    [Fact]
    public async Task CreateBatch_rejects_duplicate_member_ids_in_request()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-DUP", 1);

        var result = await CreateService(db, CenterA).CreateBatchAsync(
            new CreateRecruitmentExportBatchRequest
            {
                MemberIds = [memberId, memberId]
            });

        Assert.Equal(1, result.RecordCount);
        Assert.Contains(result.Exclusions, e => e.MemberId == memberId && e.ReasonAr.Contains("تكرار"));
    }

    [Fact]
    public async Task CreateBatch_failure_leaves_members_unexported()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-FAIL", 1);

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.ValidateFile(It.IsAny<IFormFile>(), out It.Ref<string>.IsAny))
            .Returns(true);
        storage.Setup(s => s.SaveFileAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("disk full"));
        storage.Setup(s => s.DeleteFileAsync(It.IsAny<string>())).ReturnsAsync(true);

        var service = CreateService(db, CenterA, fileStorage: storage.Object);

        await Assert.ThrowsAsync<RecruitmentExportGenerationFailedException>(() =>
            service.CreateBatchAsync(new CreateRecruitmentExportBatchRequest
            {
                MemberIds = [memberId]
            }));

        var member = await db.Members.SingleAsync(m => m.MemberId == memberId);
        Assert.Equal(FinalDecisionService.ExportStatusUnexported, member.ExportStatus);
        Assert.Null(member.LastExportedAt);

        var batch = await db.RecruitmentExportBatches.SingleAsync();
        Assert.Equal(RecruitmentExportService.StatusFailed, batch.Status);
        Assert.Null(batch.GeneratedFileId);
        Assert.NotNull(batch.CompletedAt);

        Assert.Contains(await db.AuditEvents.ToListAsync(),
            e => e.EventType == RecruitmentExportService.BatchFailedEventType);
    }

    [Fact]
    public async Task CreateBatch_excludes_member_that_lost_eligibility()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var eligible = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-KEEP", 1);
        var lost = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-LOST", 1);
        var lostMember = await db.Members.SingleAsync(m => m.MemberId == lost);
        lostMember.ExportStatus = FinalDecisionService.ExportStatusExported;
        await db.SaveChangesAsync();

        var result = await CreateService(db, CenterA).CreateBatchAsync(
            new CreateRecruitmentExportBatchRequest
            {
                MemberIds = [eligible, lost]
            });

        Assert.Equal(1, result.RecordCount);
        Assert.Contains(result.Exclusions, e => e.MemberId == lost);
        Assert.Equal(
            FinalDecisionService.ExportStatusExported,
            (await db.Members.SingleAsync(m => m.MemberId == eligible)).ExportStatus);
    }

    [Fact]
    public async Task Reexport_links_previous_completed_batch()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-RE", 1);

        var first = await CreateService(db, CenterA).CreateBatchAsync(
            new CreateRecruitmentExportBatchRequest { MemberIds = [memberId] });

        var member = await db.Members.SingleAsync(m => m.MemberId == memberId);
        member.ExportStatus = FinalDecisionService.ExportStatusUnexported;
        member.LastExportedAt = null;
        var decision = await db.FinalDecisions.SingleAsync(d => d.MemberId == memberId);
        decision.ResultStatusId = 2;
        decision.RejectionReason = "رفض بعد تعديل";
        await db.SaveChangesAsync();

        var second = await CreateService(db, CenterA).CreateBatchAsync(
            new CreateRecruitmentExportBatchRequest { MemberIds = [memberId] });

        Assert.NotEqual(first.ExportBatchId, second.ExportBatchId);
        var reexportItem = await db.RecruitmentExportItems.SingleAsync(
            i => i.ExportBatchId == second.ExportBatchId);
        Assert.True(reexportItem.IsReexport);
        Assert.Equal(first.ExportBatchId, reexportItem.PreviousExportBatchId);

        Assert.Contains(await db.AuditEvents.ToListAsync(),
            e => e.EventType == RecruitmentExportService.ReexportEventType);
    }

    [Fact]
    public async Task Download_completed_batch_returns_bytes_and_audits()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-DL", 1);
        var storage = CreateStorageMock(out _, content: [0x50, 0x4B, 0x03, 0x04, 0x00]);

        var created = await CreateService(db, CenterA, fileStorage: storage.Object)
            .CreateBatchAsync(new CreateRecruitmentExportBatchRequest { MemberIds = [memberId] });

        var file = await CreateService(db, CenterA, fileStorage: storage.Object)
            .DownloadBatchFileAsync(created.ExportBatchId);

        Assert.NotEmpty(file.FileContents);
        Assert.Contains(".xlsx", file.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(await db.AuditEvents.ToListAsync(),
            e => e.EventType == RecruitmentExportService.BatchDownloadedEventType
                 && e.NewValues != null
                 && !e.NewValues.Contains("StorageKey", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Download_rejects_other_center_batch()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-XCTR", 1);
        var created = await CreateService(db, CenterA).CreateBatchAsync(
            new CreateRecruitmentExportBatchRequest { MemberIds = [memberId] });

        await Assert.ThrowsAsync<RecruitmentExportBatchNotFoundException>(() =>
            CreateService(db, CenterB).DownloadBatchFileAsync(created.ExportBatchId));
    }

    [Fact]
    public async Task History_lists_completed_items_only_for_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var a = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-H1", 1);
        await CreateService(db, CenterA).CreateBatchAsync(
            new CreateRecruitmentExportBatchRequest { MemberIds = [a] });

        // دفعة فاشلة لا تظهر في سجل المصدَّر.
        var failId = await CreateMemberWithDecisionAsync(db, CenterA, "EXP-H2", 1);
        var failStorage = new Mock<IFileStorageService>();
        failStorage.Setup(s => s.ValidateFile(It.IsAny<IFormFile>(), out It.Ref<string>.IsAny)).Returns(true);
        failStorage.Setup(s => s.SaveFileAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        failStorage.Setup(s => s.DeleteFileAsync(It.IsAny<string>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<RecruitmentExportGenerationFailedException>(() =>
            CreateService(db, CenterA, fileStorage: failStorage.Object).CreateBatchAsync(
                new CreateRecruitmentExportBatchRequest { MemberIds = [failId] }));

        var history = await CreateService(db, CenterA, allowRecruitment: true, allowHistory: true)
            .ListHistoryAsync(new RecruitmentExportHistoryQuery());

        Assert.Equal(1, history.TotalCount);
        Assert.Equal(a, history.Items.Single().MemberId);
    }

    [Fact]
    public async Task History_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, allowRecruitment: true, allowHistory: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ListHistoryAsync(new RecruitmentExportHistoryQuery()));
    }

    private static Mock<IFileStorageService> CreateStorageMock(
        out List<string> savedPaths,
        byte[]? content = null)
    {
        var paths = new List<string>();
        savedPaths = paths;
        var bytes = content ?? [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.ValidateFile(It.IsAny<IFormFile>(), out It.Ref<string>.IsAny))
            .Returns(true);
        storage.Setup(s => s.SaveFileAsync(It.IsAny<IFormFile>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((IFormFile file, string _, string __) =>
            {
                var path = $"storage/uploads/RecruitmentExportBatch/{Guid.NewGuid():N}.xlsx";
                paths.Add(path);
                return (path, file.Length);
            });
        storage.Setup(s => s.ReadFileBytes(It.IsAny<string>())).Returns(bytes);
        storage.Setup(s => s.DeleteFileAsync(It.IsAny<string>())).ReturnsAsync(true);
        return storage;
    }

    private static FinalDecisionService CreateFinalDecisionService(AppDbContext db, Guid centerId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Clinical.FinalDecision,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-1", "actor", "127.0.0.1");

        var postgresSession = new Mock<IPostgresSessionGuard>();
        postgresSession.SetupGet(s => s.DefersFinalDecisionHistoryToDatabase).Returns(false);
        postgresSession.Setup(s => s.ApplyForFinalDecisionWriteAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new FinalDecisionService(
            gate.Object,
            currentUser,
            new MemberRepository(db),
            new ClinicWorkspaceRepository(db),
            new CenterClinicRepository(db),
            new ReferenceDataRepository(db),
            new MedicalCenterRepository(db),
            new MemberArchiveRepository(db),
            new AuditLogRepository(db),
            new ExportTestUnitOfWork(db),
            postgresSession.Object);
    }

    private static RecruitmentExportService CreateService(
        AppDbContext db,
        Guid centerId,
        bool allowRecruitment = true,
        bool allowHistory = true,
        IFileStorageService? fileStorage = null)
    {
        var gate = new Mock<IPermissionGate>();
        if (allowRecruitment)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Export.Recruitment,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(centerId);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Export.Recruitment,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        if (allowHistory)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Export.ViewHistory,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(centerId);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Export.ViewHistory,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-1", "actor", "127.0.0.1");

        fileStorage ??= CreateStorageMock(out _).Object;

        return new RecruitmentExportService(
            gate.Object,
            currentUser,
            new MemberRepository(db),
            new ClinicWorkspaceRepository(db),
            new MedicalCenterRepository(db),
            new ReferenceDataRepository(db),
            new RecruitmentExportRepository(db),
            new AttachmentRepository(db),
            new ExportService(),
            fileStorage,
            new AuditLogRepository(db),
            new ExportTestUnitOfWork(db));
    }

    private static async Task<Guid> CreateMemberWithDecisionAsync(
        AppDbContext db,
        Guid centerId,
        string recruitment,
        short resultStatusId,
        string? rejectionReason = null)
    {
        var memberId = await CreateMemberAsync(db, centerId, recruitment);
        await SeedAllAssessmentsAsync(db, memberId, centerId, resultStatusId == 3 ? (short)1 : resultStatusId);

        var now = DateTimeOffset.UtcNow;
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = centerId,
            SupervisorUserId = "actor-1",
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });

        // يحاكي اعتماد القرار النهائي: النسخة الحالية مُراجَعة قبل الظهور في قائمة التصدير.
        var member = await db.Members.SingleAsync(m => m.MemberId == memberId);
        member.ReviewedRevision = member.DataRevision;
        member.ReviewedAt = now;
        member.ReviewedByUserId = "actor-1";

        await db.SaveChangesAsync();
        return memberId;
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, Guid centerId, string recruitment)
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
        return created.MemberId;
    }

    private static async Task SeedAllAssessmentsAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        short resultStatusId)
    {
        foreach (var clinicId in AllClinics)
            await SeedAssessmentAsync(db, memberId, centerId, clinicId, resultStatusId);
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

        await db.SaveChangesAsync();
    }

    private sealed class ExportTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public ExportTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new ExportNoopTransaction());
    }

    private sealed class ExportNoopTransaction : IDbContextTransaction
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
