using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Dtos.Supervisor;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services;
using Application.Services.Supervisor;
using ClosedXML.Excel;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class SupervisorFollowUpServiceTests
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
    public async Task List_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, allow: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ListAsync(new SupervisorFollowUpQuery()));
    }

    [Fact]
    public async Task List_isolates_centers()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var inA = await CreateMemberAsync(db, CenterA, "FU-A", "منتسب أ");
        await CreateMemberAsync(db, CenterB, "FU-B", "منتسب ب");

        var listA = await CreateService(db, CenterA, allow: true).ListAsync(new SupervisorFollowUpQuery());
        Assert.Equal(1, listA.TotalCount);
        Assert.Equal(inA, Assert.Single(listA.Items).MemberId);

        var listB = await CreateService(db, CenterB, allow: true).ListAsync(new SupervisorFollowUpQuery());
        Assert.Equal(1, listB.TotalCount);
        Assert.Equal("منتسب ب", Assert.Single(listB.Items).FullName);
    }

    [Fact]
    public async Task List_searches_by_file_number_name_recruitment_and_barcode()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var first = await CreateMemberAsync(db, CenterA, "REC-100", "أحمد علي");
        await CreateMemberAsync(db, CenterA, "REC-200", "سارة محمد");

        var service = CreateService(db, CenterA, allow: true);
        var byName = await service.ListAsync(new SupervisorFollowUpQuery { FullName = "أحمد" });
        Assert.Equal(first, Assert.Single(byName.Items).MemberId);

        var byRecruitment = await service.ListAsync(new SupervisorFollowUpQuery { RecruitmentNumber = "rec-100" });
        Assert.Equal(first, Assert.Single(byRecruitment.Items).MemberId);

        var member = await db.Members.SingleAsync(m => m.MemberId == first);
        var byFile = await service.ListAsync(new SupervisorFollowUpQuery { FileNumber = member.FileNumber });
        Assert.Equal(first, Assert.Single(byFile.Items).MemberId);

        var byBarcode = await service.ListAsync(new SupervisorFollowUpQuery
        {
            Barcode = member.FileNumber.ToString()
        });
        Assert.Equal(first, Assert.Single(byBarcode.Items).MemberId);
        Assert.Equal(member.FileNumber.ToString(), byBarcode.Items.First().Barcode);
    }

    [Fact]
    public async Task List_marks_ready_and_incomplete()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var readyId = await CreateMemberAsync(db, CenterA, "FU-READY", "جاهز");
        await SeedAllAssessmentsAsync(db, readyId, CenterA, 1);

        var incompleteId = await CreateMemberAsync(db, CenterA, "FU-INC", "ناقص");
        await SeedAssessmentAsync(db, incompleteId, CenterA, ClinicEyes, 1);

        var service = CreateService(db, CenterA, allow: true);

        var ready = await service.ListAsync(new SupervisorFollowUpQuery
        {
            Status = SupervisorFollowUpStatuses.Ready
        });
        var readyItem = Assert.Single(ready.Items);
        Assert.Equal(readyId, readyItem.MemberId);
        Assert.True(readyItem.IsReadyToDecide);
        Assert.True(readyItem.CanIssueFinalDecision);
        Assert.True(readyItem.AreMandatoryClinicsComplete);
        Assert.Equal(SupervisorFollowUpStatuses.Ready, readyItem.FollowUpStatus);
        Assert.Empty(readyItem.MissingClinicCodes);

        var incomplete = await service.ListAsync(new SupervisorFollowUpQuery
        {
            Status = SupervisorFollowUpStatuses.Incomplete
        });
        var incompleteItem = Assert.Single(incomplete.Items);
        Assert.Equal(incompleteId, incompleteItem.MemberId);
        Assert.False(incompleteItem.AreMandatoryClinicsComplete);
        Assert.Contains("ENT", incompleteItem.MissingClinicCodes);
        Assert.Equal(SupervisorFollowUpStatuses.Incomplete, incompleteItem.FollowUpStatus);
    }

    [Fact]
    public async Task List_filters_missing_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var missingEnt = await CreateMemberAsync(db, CenterA, "FU-MISS", "ينقصه أذنية");
        await SeedAssessmentAsync(db, missingEnt, CenterA, ClinicEyes, 1);

        var complete = await CreateMemberAsync(db, CenterA, "FU-FULL", "مكتمل");
        await SeedAllAssessmentsAsync(db, complete, CenterA, 1);

        var service = CreateService(db, CenterA, allow: true);
        var page = await service.ListAsync(new SupervisorFollowUpQuery
        {
            MissingClinicId = ClinicEnt
        });

        Assert.Equal(missingEnt, Assert.Single(page.Items).MemberId);
    }

    [Fact]
    public async Task List_filters_deferred_and_action_type()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var deferredId = await CreateMemberAsync(db, CenterA, "FU-DEF", "مؤجل");
        await SeedAllAssessmentsAsync(db, deferredId, CenterA, 3);
        var decisionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = decisionId,
            MemberId = deferredId,
            CenterId = CenterA,
            SupervisorUserId = "actor-1",
            ResultStatusId = 3,
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
        db.DeferredActions.Add(new DeferredAction
        {
            DeferredActionId = Guid.NewGuid(),
            MemberId = deferredId,
            CenterId = CenterA,
            FinalDecisionId = decisionId,
            DeferReason = "استمارة",
            DeferUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
            ActionTypeId = 1,
            FollowupNote = "متابعة",
            Status = "REQUESTED",
            RequestedByUserId = "actor-1",
            RequestedAt = now
        });
        await db.SaveChangesAsync();

        var acceptedId = await CreateMemberAsync(db, CenterA, "FU-ACC", "مقبول");
        await SeedAllAssessmentsAsync(db, acceptedId, CenterA, 1);
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = Guid.NewGuid(),
            MemberId = acceptedId,
            CenterId = CenterA,
            SupervisorUserId = "actor-1",
            ResultStatusId = 1,
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, CenterA, allow: true);
        var deferred = await service.ListAsync(new SupervisorFollowUpQuery
        {
            Status = SupervisorFollowUpStatuses.Deferred
        });
        var item = Assert.Single(deferred.Items);
        Assert.Equal(deferredId, item.MemberId);
        Assert.True(item.IsDeferred);
        Assert.Equal((short)3, item.FinalResultStatusId);
        Assert.NotNull(item.OpenDeferredActionId);
        Assert.Equal("REQUESTED", item.OpenDeferredStatus);
        Assert.Equal("طلب استمارة", item.OpenDeferredActionTypeNameAr);

        var byAction = await service.ListAsync(new SupervisorFollowUpQuery
        {
            ActionTypeId = 1,
            ActionStatus = "REQUESTED"
        });
        Assert.Equal(deferredId, Assert.Single(byAction.Items).MemberId);
    }

    [Fact]
    public async Task List_filters_decided_amended_and_result()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var decidedId = await CreateMemberAsync(db, CenterA, "FU-DEC", "معتمد");
        await SeedAllAssessmentsAsync(db, decidedId, CenterA, 1);
        var now = DateTimeOffset.UtcNow;
        var decidedDecisionId = Guid.NewGuid();
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = decidedDecisionId,
            MemberId = decidedId,
            CenterId = CenterA,
            SupervisorUserId = "actor-1",
            ResultStatusId = 1,
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });

        var amendedId = await CreateMemberAsync(db, CenterA, "FU-AMD", "معدّل");
        await SeedAllAssessmentsAsync(db, amendedId, CenterA, 2);
        var amendedDecisionId = Guid.NewGuid();
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = amendedDecisionId,
            MemberId = amendedId,
            CenterId = CenterA,
            SupervisorUserId = "actor-1",
            ResultStatusId = 2,
            RejectionReason = "رفض بعد تعديل",
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
        db.FinalDecisionHistories.Add(new FinalDecisionHistory
        {
            HistoryId = Guid.NewGuid(),
            FinalDecisionId = amendedDecisionId,
            MemberId = amendedId,
            CenterId = CenterA,
            OldStatusId = 1,
            NewStatusId = 2,
            ChangeReason = "نتائج جديدة",
            ChangedByUserId = "actor-1",
            ChangedAt = now,
            ExportResetPerformed = true
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, CenterA, allow: true);

        var decided = await service.ListAsync(new SupervisorFollowUpQuery
        {
            Status = SupervisorFollowUpStatuses.Decided
        });
        Assert.Equal(decidedId, Assert.Single(decided.Items).MemberId);
        Assert.Equal(SupervisorFollowUpStatuses.Decided, decided.Items.First().FollowUpStatus);

        var amended = await service.ListAsync(new SupervisorFollowUpQuery
        {
            Status = SupervisorFollowUpStatuses.Amended
        });
        var amendedItem = Assert.Single(amended.Items);
        Assert.Equal(amendedId, amendedItem.MemberId);
        Assert.True(amendedItem.IsAmended);
        Assert.Equal(SupervisorFollowUpStatuses.Amended, amendedItem.FollowUpStatus);

        var rejected = await service.ListAsync(new SupervisorFollowUpQuery
        {
            ResultStatusId = 2
        });
        Assert.Equal(amendedId, Assert.Single(rejected.Items).MemberId);
    }

    [Fact]
    public async Task List_filters_by_created_date_range_and_paginates()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var older = await CreateMemberAsync(db, CenterA, "FU-OLD", "قديم");
        var newer = await CreateMemberAsync(db, CenterA, "FU-NEW", "جديد");

        var olderRow = await db.Members.SingleAsync(m => m.MemberId == older);
        olderRow.CreatedAt = DateTimeOffset.UtcNow.AddDays(-10);
        olderRow.UpdatedAt = olderRow.CreatedAt;
        var newerRow = await db.Members.SingleAsync(m => m.MemberId == newer);
        newerRow.CreatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        newerRow.UpdatedAt = newerRow.CreatedAt;
        await db.SaveChangesAsync();

        var service = CreateService(db, CenterA, allow: true);
        var ranged = await service.ListAsync(new SupervisorFollowUpQuery
        {
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow),
            SortBy = SupervisorFollowUpService.SortCreatedAtDesc
        });
        Assert.Equal(newer, Assert.Single(ranged.Items).MemberId);

        var page1 = await service.ListAsync(new SupervisorFollowUpQuery
        {
            PageNumber = 1,
            PageSize = 1,
            SortBy = SupervisorFollowUpService.SortFileNumberAsc
        });
        Assert.Equal(2, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Single(page1.Items);
    }

    [Fact]
    public async Task List_rejects_invalid_status_and_does_not_expose_clinical_blobs()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "FU-SAFE", "آمن");
        await SeedAllAssessmentsAsync(db, memberId, CenterA, 1);

        var service = CreateService(db, CenterA, allowList: true);
        await Assert.ThrowsAsync<InvalidSupervisorFollowUpDataException>(() =>
            service.ListAsync(new SupervisorFollowUpQuery { Status = "UNKNOWN" }));

        var page = await service.ListAsync(new SupervisorFollowUpQuery());
        var item = Assert.Single(page.Items);
        Assert.Null(item.GetType().GetProperty("ClinicalNotes"));
        Assert.Null(item.GetType().GetProperty("StorageKey"));
        Assert.Equal(memberId, item.MemberId);
        Assert.False(string.IsNullOrWhiteSpace(item.FollowUpStatus));
    }

    [Fact]
    public async Task List_filters_by_export_status()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var unexportedId = await CreateMemberAsync(db, CenterA, "FU-UNEX", "غير مصدّر");
        var exportedId = await CreateMemberAsync(db, CenterA, "FU-EX", "مصدّر");

        var exported = await db.Members.SingleAsync(m => m.MemberId == exportedId);
        exported.ExportStatus = FinalDecisionService.ExportStatusExported;
        exported.LastExportedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var service = CreateService(db, CenterA, allowList: true);
        var unexported = await service.ListAsync(new SupervisorFollowUpQuery
        {
            ExportStatus = FinalDecisionService.ExportStatusUnexported
        });
        Assert.Equal(unexportedId, Assert.Single(unexported.Items).MemberId);

        var exportedPage = await service.ListAsync(new SupervisorFollowUpQuery
        {
            ExportStatus = FinalDecisionService.ExportStatusExported
        });
        Assert.Equal(exportedId, Assert.Single(exportedPage.Items).MemberId);
    }

    [Fact]
    public async Task Export_without_filters_matches_list_and_writes_audit()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var first = await CreateMemberAsync(db, CenterA, "FU-X1", "تصدير واحد");
        var second = await CreateMemberAsync(db, CenterA, "FU-X2", "تصدير اثنان");
        await SeedAllAssessmentsAsync(db, first, CenterA, 1);

        var service = CreateService(db, CenterA, allowList: true, allowExport: true);
        var list = await service.ListAsync(new SupervisorFollowUpQuery
        {
            SortBy = SupervisorFollowUpService.SortFileNumberAsc,
            PageSize = 200
        });
        var file = await service.ExportAsync(new SupervisorFollowUpQuery
        {
            SortBy = SupervisorFollowUpService.SortFileNumberAsc
        });

        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.ContentType);
        Assert.True(file.FileContents.Length > 100);
        Assert.Equal(2, list.TotalCount);
        Assert.Equal(new[] { first, second }.OrderBy(id => id).ToArray(),
            list.Items.Select(i => i.MemberId).OrderBy(id => id).ToArray());

        using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
        var sheet = workbook.Worksheets.First();
        // ExportService: title rows 1-4, headers row 5, data from row 6.
        const int dataStartRow = 6;
        var listItems = list.Items.ToList();
        Assert.Equal(listItems[0].FileNumber, sheet.Cell(dataStartRow, 2).GetValue<long>());
        Assert.Equal(listItems[0].FullName, sheet.Cell(dataStartRow, 3).GetString());
        Assert.Equal(listItems[1].FileNumber, sheet.Cell(dataStartRow + 1, 2).GetValue<long>());
        Assert.Equal(listItems[1].FullName, sheet.Cell(dataStartRow + 1, 3).GetString());
        Assert.Equal("رقم الدور", sheet.Cell(5, 1).GetString());
        Assert.Equal("رقم الملف", sheet.Cell(5, 2).GetString());

        var audit = Assert.Single(await db.AuditEvents
            .Where(e => e.EventType == SupervisorFollowUpService.ExportEventType)
            .ToListAsync());
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal(CenterA, audit.CenterId);
        Assert.DoesNotContain("تصدير واحد", audit.NewValues ?? string.Empty);
        Assert.Contains("\"RecordCount\":2", audit.NewValues ?? string.Empty);
    }

    [Fact]
    public async Task Export_applies_result_and_date_filters_like_list()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var acceptedId = await CreateMemberAsync(db, CenterA, "FU-XR1", "مقبول للتصدير");
        await SeedAllAssessmentsAsync(db, acceptedId, CenterA, 1);
        var now = DateTimeOffset.UtcNow;
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = Guid.NewGuid(),
            MemberId = acceptedId,
            CenterId = CenterA,
            SupervisorUserId = "actor-1",
            ResultStatusId = 1,
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });

        var rejectedId = await CreateMemberAsync(db, CenterA, "FU-XR2", "مرفوض للتصدير");
        await SeedAllAssessmentsAsync(db, rejectedId, CenterA, 2);
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = Guid.NewGuid(),
            MemberId = rejectedId,
            CenterId = CenterA,
            SupervisorUserId = "actor-1",
            ResultStatusId = 2,
            RejectionReason = "سبب رفض",
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var acceptedMember = await db.Members.SingleAsync(m => m.MemberId == acceptedId);
        acceptedMember.CreatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        var service = CreateService(db, CenterA, allowList: true, allowExport: true);
        var query = new SupervisorFollowUpQuery
        {
            ResultStatusId = 1,
            FromDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
            ToDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var list = await service.ListAsync(query);
        Assert.Equal(acceptedId, Assert.Single(list.Items).MemberId);

        var file = await service.ExportAsync(query);
        using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
        var text = string.Join(
            " ",
            workbook.Worksheet(1).CellsUsed().Select(c => c.GetString()));
        Assert.Contains(acceptedMember.FileNumber.ToString(), text);
        Assert.DoesNotContain("مرفوض للتصدير", text);
    }

    [Fact]
    public async Task Export_rejects_without_permission_and_isolates_centers()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberAsync(db, CenterA, "FU-XP-A", "مركز أ");
        await CreateMemberAsync(db, CenterB, "FU-XP-B", "مركز ب");

        var denied = CreateService(db, CenterA, allowList: true, allowExport: false);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            denied.ExportAsync(new SupervisorFollowUpQuery()));

        var exportA = await CreateService(db, CenterA, allowList: true, allowExport: true)
            .ExportAsync(new SupervisorFollowUpQuery());
        using (var workbook = new XLWorkbook(new MemoryStream(exportA.FileContents)))
        {
            var text = string.Join(" ", workbook.Worksheet(1).CellsUsed().Select(c => c.GetString()));
            Assert.Contains("مركز أ", text);
            Assert.DoesNotContain("مركز ب", text);
        }

        var exportB = await CreateService(db, CenterB, allowList: true, allowExport: true)
            .ExportAsync(new SupervisorFollowUpQuery());
        using (var workbook = new XLWorkbook(new MemoryStream(exportB.FileContents)))
        {
            var text = string.Join(" ", workbook.Worksheet(1).CellsUsed().Select(c => c.GetString()));
            Assert.Contains("مركز ب", text);
            Assert.DoesNotContain("مركز أ", text);
        }
    }

    [Fact]
    public async Task Export_empty_results_returns_clear_message()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberAsync(db, CenterA, "FU-EMPTY", "موجود");

        var service = CreateService(db, CenterA, allowList: true, allowExport: true);
        var error = await Assert.ThrowsAsync<SupervisorFollowUpExportEmptyException>(() =>
            service.ExportAsync(new SupervisorFollowUpQuery
            {
                FullName = "لا يوجد بهذا الاسم"
            }));
        Assert.Equal("لا توجد نتائج ضمن التصفية الحالية.", error.Message);
        Assert.Equal(0, await db.AuditEvents.CountAsync(e =>
            e.EventType == SupervisorFollowUpService.ExportEventType));
    }

    #region Helpers

    private static SupervisorFollowUpService CreateService(
        AppDbContext db,
        Guid centerId,
        bool allow = true,
        bool allowList = true,
        bool allowExport = false)
    {
        // Preserve older call sites that pass allow: true/false for list permission.
        if (!allow)
            allowList = false;

        var gate = new Mock<IPermissionGate>();
        if (allowList)
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

        if (allowExport)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.Export,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(centerId);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.Export,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        return new SupervisorFollowUpService(
            gate.Object,
            new TestCurrentUser(),
            new MemberRepository(db),
            new ClinicWorkspaceRepository(db),
            new CenterClinicRepository(db),
            new ReferenceDataRepository(db),
            new MedicalCenterRepository(db),
            new ExportService(),
            new AuditLogRepository(db),
            new FollowUpTestUnitOfWork(db));
    }

    private sealed class FollowUpTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public FollowUpTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new FollowUpNoopTransaction());
    }

    private sealed class FollowUpNoopTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<Guid> CreateMemberAsync(
        AppDbContext db,
        Guid centerId,
        string recruitment,
        string fullName)
    {
        var members = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
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
            ClinicalNotes = "ملاحظات سرية للاختبار فقط",
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

    #endregion
}
