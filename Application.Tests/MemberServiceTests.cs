using System.Text.Json;
using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Mappings;
using Application.Services;
using Application.Services.Members;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class MemberServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Create_assigns_file_and_queue_numbers_identity_and_audit()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var created = await service.CreateAsync(ValidCreate());

        Assert.Equal(1, created.FileNumber);
        Assert.Equal(1, created.QueueNumber);
        Assert.Equal(CenterA, created.CurrentMedicalCenterId);
        Assert.Equal(22.8m, created.Bmi);
        Assert.Equal("طبيعي", created.BmiCategoryNameAr);

        var identity = await db.MemberCenterIdentities.SingleAsync();
        Assert.Equal(created.MemberId, identity.MemberId);
        Assert.Equal(CenterA, identity.MedicalCenterId);
        Assert.Equal("1", identity.CenterMemberId);
        Assert.Null(identity.ValidTo);

        var audit = await db.AuditEvents.SingleAsync(e => e.EventType == "MemberCreate");
        Assert.Equal(nameof(Member), audit.EntityType);
        Assert.Equal(created.MemberId.ToString(), audit.EntityId);
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal(CenterA, audit.CenterId);
    }

    [Fact]
    public async Task Create_second_member_same_day_increments_queue_and_file()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var first = await service.CreateAsync(ValidCreate(recruitment: "R-1"));
        var second = await service.CreateAsync(ValidCreate(recruitment: "R-2", fullName: "منتسب ثاني"));

        Assert.Equal(1, first.FileNumber);
        Assert.Equal(1, first.QueueNumber);
        Assert.Equal(2, second.FileNumber);
        Assert.Equal(2, second.QueueNumber);
    }

    [Fact]
    public async Task Create_queue_numbers_are_independent_per_center()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);

        var serviceA = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var inA = await serviceA.CreateAsync(ValidCreate(recruitment: "QA-1", fullName: "في أ"));

        var serviceB = CreateService(db, CenterB, allowCreate: true, allowView: true);
        var inB = await serviceB.CreateAsync(ValidCreate(recruitment: "QB-1", fullName: "في ب"));

        Assert.Equal(1, inA.QueueNumber);
        Assert.Equal(1, inB.QueueNumber);
        Assert.Equal(CenterA, inA.CurrentMedicalCenterId);
        Assert.Equal(CenterB, inB.CurrentMedicalCenterId);
        Assert.NotEqual(inA.FileNumber, inB.FileNumber);
    }

    [Fact]
    public async Task Create_maps_recruitment_unique_db_conflict_to_business_exception()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);

        var existsCalls = 0;
        var members = new Mock<IMemberRepository>();
        members.Setup(m => m.RecruitmentNumberExistsAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref existsCalls) > 1);
        members.Setup(m => m.AllocateFileAndQueueNumbersAsync(
                CenterA,
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((1L, 1));
        members.Setup(m => m.Add(It.IsAny<Member>()));
        members.Setup(m => m.AddIdentity(It.IsAny<MemberCenterIdentity>()));

        var audit = new Mock<IAuditLogRepository>();
        audit.Setup(a => a.Add(It.IsAny<AuditEvent>()));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoopTransaction());
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("unique recruitment", innerException: null));

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Members.CreateUpdate,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CenterA);

        var config = new TypeAdapterConfig();
        new MemberProfile().Register(config);
        var service = new MemberService(
            members.Object,
            Mock.Of<IMemberImportBatchRepository>(),
            new MedicalCenterRepository(db),
            new ReferenceDataRepository(db),
            Mock.Of<IClinicWorkspaceRepository>(),
            audit.Object,
            Mock.Of<ICrossCenterAccessService>(),
            uow.Object,
            new Mapper(config),
            gate.Object,
            new TestCurrentUser(),
            Mock.Of<IExportService>());

        var error = await Assert.ThrowsAsync<MemberRecruitmentNumberExistsException>(() =>
            service.CreateAsync(ValidCreate(recruitment: "RACE-1")));

        Assert.Equal(400, error.Status);
        Assert.Equal("MEMBER_RECRUITMENT_NUMBER_EXISTS", error.ErrorCode);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_preserves_file_number_and_center_and_writes_audit()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var created = await service.CreateAsync(ValidCreate());
        var createdAt = (await db.Members.SingleAsync(m => m.MemberId == created.MemberId)).CreatedAt;

        var updated = await service.UpdateAsync(created.MemberId, new UpdateMemberRequest
        {
            FullName = "اسم معدّل",
            MotherName = created.MotherName,
            BirthDate = created.BirthDate,
            MaritalStatusId = created.MaritalStatusId,
            Occupation = "مهنة جديدة",
            RecruitmentNumber = created.RecruitmentNumber,
            RecruitmentCenterId = created.RecruitmentCenterId,
            BloodTypeId = created.BloodTypeId,
            HeightCm = 180,
            WeightKg = 80,
            HasTattoo = false
        });

        Assert.Equal(created.FileNumber, updated.FileNumber);
        Assert.Equal(created.QueueNumber, updated.QueueNumber);
        Assert.Equal(created.CurrentMedicalCenterId, updated.CurrentMedicalCenterId);
        Assert.Equal("اسم معدّل", updated.FullName);
        Assert.Equal(2, updated.DataRevision);
        Assert.Equal(24.7m, updated.Bmi);
        Assert.Equal("طبيعي", updated.BmiCategoryNameAr);

        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal(created.FileNumber, row.FileNumber);
        Assert.Equal(created.QueueNumber, row.QueueNumber);
        Assert.Equal(created.QueueDate, row.QueueDate);
        Assert.Equal(CenterA, row.CurrentMedicalCenterId);
        Assert.Equal("actor-1", row.UpdatedByUserId);
        Assert.True(row.UpdatedAt >= createdAt);

        var audit = Assert.Single(await db.AuditEvents.Where(e => e.EventType == "MemberUpdate").ToListAsync());
        Assert.False(string.IsNullOrWhiteSpace(audit.OldValues));
        Assert.False(string.IsNullOrWhiteSpace(audit.NewValues));

        using var oldJson = System.Text.Json.JsonDocument.Parse(audit.OldValues!);
        using var newJson = System.Text.Json.JsonDocument.Parse(audit.NewValues!);
        Assert.Equal(created.FullName, oldJson.RootElement.GetProperty("FullName").GetString());
        Assert.Equal("اسم معدّل", newJson.RootElement.GetProperty("FullName").GetString());
        Assert.Equal(created.FileNumber, oldJson.RootElement.GetProperty("FileNumber").GetInt64());
        Assert.Equal(created.FileNumber, newJson.RootElement.GetProperty("FileNumber").GetInt64());
        Assert.Equal(1, oldJson.RootElement.GetProperty("DataRevision").GetInt64());
        Assert.Equal(2, newJson.RootElement.GetProperty("DataRevision").GetInt64());
        Assert.False(oldJson.RootElement.TryGetProperty("password", out _));
        Assert.False(newJson.RootElement.TryGetProperty("SecurityStamp", out _));
    }

    [Fact]
    public async Task Update_from_other_center_is_not_found()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var createService = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await createService.CreateAsync(ValidCreate());

        var updateB = CreateService(db, CenterB, allowCreate: true, allowView: true);
        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            updateB.UpdateAsync(created.MemberId, ToUpdate(created, fullName: "محاولة مركز آخر")));

        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal(created.FullName, row.FullName);
        Assert.Equal(1, row.DataRevision);
        Assert.Empty(await db.AuditEvents.Where(e => e.EventType == "MemberUpdate").ToListAsync());
    }

    [Fact]
    public async Task Update_without_final_decision_succeeds_with_create_update_permission()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "NO-FD-1"));

        var updated = await service.UpdateAsync(
            created.MemberId,
            ToUpdate(created, fullName: "بعد التعديل بلا قرار"));

        Assert.Equal("بعد التعديل بلا قرار", updated.FullName);
        Assert.Equal(2, updated.DataRevision);
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == MemberService.MemberUpdateEventType));
    }

    [Fact]
    public async Task Update_with_final_decision_without_admin_permission_is_forbidden()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "FD-FORB"));
        await SeedFinalDecisionAsync(db, created.MemberId, CenterA);

        var request = ToUpdate(created, fullName: "محاولة ممنوعة");
        request.EditReason = "سبب لن يُقبل بدون صلاحية";

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.UpdateAsync(created.MemberId, request));

        Assert.Equal(403, error.Status);
        db.ChangeTracker.Clear();
        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal(created.FullName, row.FullName);
        Assert.Equal(1, row.DataRevision);
        Assert.Empty(await db.AuditEvents.Where(e =>
            e.EventType == MemberService.MemberUpdateAfterFinalDecisionEventType).ToListAsync());
    }

    [Fact]
    public async Task Update_with_final_decision_requires_non_empty_edit_reason()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(
            db, CenterA, allowCreate: true, allowView: true, allowEditAfterFinalDecision: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "FD-REASON"));
        await SeedFinalDecisionAsync(db, created.MemberId, CenterA);

        var blank = ToUpdate(created, fullName: "بدون سبب");
        blank.EditReason = "   ";
        var blankError = await Assert.ThrowsAsync<InvalidMemberDataException>(() =>
            service.UpdateAsync(created.MemberId, blank));
        Assert.Equal(400, blankError.Status);

        var tooLong = ToUpdate(created, fullName: "سبب طويل");
        tooLong.EditReason = new string('س', MemberService.MaxEditReasonLength + 1);
        var lengthError = await Assert.ThrowsAsync<InvalidMemberDataException>(() =>
            service.UpdateAsync(created.MemberId, tooLong));
        Assert.Equal(400, lengthError.Status);

        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.Members.SingleAsync(m => m.MemberId == created.MemberId)).DataRevision);
    }

    [Fact]
    public async Task Update_with_final_decision_admin_reason_succeeds_and_audits()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(
            db, CenterA, allowCreate: true, allowView: true, allowEditAfterFinalDecision: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "FD-OK"));
        var decisionId = await SeedFinalDecisionAsync(db, created.MemberId, CenterA);
        var exportBefore = (await db.Members.SingleAsync(m => m.MemberId == created.MemberId)).ExportStatus;

        var request = ToUpdate(created, fullName: "اسم إداري");
        request.EditReason = "  تصحيح بيانات تعريفية  ";

        var updated = await service.UpdateAsync(created.MemberId, request);

        Assert.Equal("اسم إداري", updated.FullName);
        Assert.Equal(created.HeightCm, updated.HeightCm);
        Assert.Equal(created.BloodTypeId, updated.BloodTypeId);

        db.ChangeTracker.Clear();
        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal(exportBefore, row.ExportStatus);
        Assert.Equal(2, row.DataRevision);

        var audit = await db.AuditEvents.SingleAsync(e =>
            e.EventType == MemberService.MemberUpdateAfterFinalDecisionEventType);
        Assert.Equal("تصحيح بيانات تعريفية", audit.ReasonText);
        Assert.Equal(CenterA, audit.CenterId);
        Assert.Equal("actor-1", audit.ActorUserId);
        using var oldJson = JsonDocument.Parse(audit.OldValues!);
        using var newJson = JsonDocument.Parse(audit.NewValues!);
        Assert.Equal(decisionId, oldJson.RootElement.GetProperty("FinalDecisionId").GetGuid());
        Assert.Equal(decisionId, newJson.RootElement.GetProperty("FinalDecisionId").GetGuid());
        Assert.Equal(created.FullName, oldJson.RootElement.GetProperty("FullName").GetString());
        Assert.Equal("اسم إداري", newJson.RootElement.GetProperty("FullName").GetString());
    }

    [Fact]
    public async Task Update_exported_member_material_change_resets_export_without_clearing_last_exported_revision()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "EXP-MAT"));
        const int exportedRevision = 5;
        var exportedAt = DateTimeOffset.UtcNow.AddDays(-1);

        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        row.DataRevision = exportedRevision;
        row.ReviewedRevision = exportedRevision;
        row.LastExportedRevision = exportedRevision;
        row.ExportStatus = "EXPORTED";
        row.LastExportedAt = exportedAt;
        row.ReviewedByUserId = "actor-1";
        row.ReviewedAt = exportedAt;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var request = ToUpdate(created, fullName: "بعد التصدير");
        await service.UpdateAsync(created.MemberId, request);

        db.ChangeTracker.Clear();
        row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal("UNEXPORTED", row.ExportStatus);
        Assert.Equal(exportedRevision + 1, row.DataRevision);
        Assert.Equal(exportedRevision, row.ReviewedRevision);
        Assert.Equal(exportedRevision, row.LastExportedRevision);
        Assert.Null(row.LastExportedAt);
        Assert.Equal("بعد التصدير", row.FullName);
    }

    [Fact]
    public async Task Update_exported_member_without_material_change_preserves_export_and_revision()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "EXP-NOOP"));
        const int exportedRevision = 8;
        var exportedAt = DateTimeOffset.UtcNow.AddDays(-2);

        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        row.DataRevision = exportedRevision;
        row.ReviewedRevision = exportedRevision;
        row.LastExportedRevision = exportedRevision;
        row.ExportStatus = "EXPORTED";
        row.LastExportedAt = exportedAt;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var request = ToUpdate(created, fullName: created.FullName);
        await service.UpdateAsync(created.MemberId, request);

        db.ChangeTracker.Clear();
        row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal("EXPORTED", row.ExportStatus);
        Assert.Equal(exportedRevision, row.DataRevision);
        Assert.Equal(exportedRevision, row.LastExportedRevision);
        Assert.Equal(exportedAt, row.LastExportedAt);
    }

    [Fact]
    public async Task Update_with_final_decision_rejects_medical_field_change()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(
            db, CenterA, allowCreate: true, allowView: true, allowEditAfterFinalDecision: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "FD-MED"));
        await SeedFinalDecisionAsync(db, created.MemberId, CenterA);

        var request = ToUpdate(created, fullName: created.FullName);
        request.EditReason = "محاولة طبية";
        request.HeightCm = (created.HeightCm ?? 170m) + 1m;

        var error = await Assert.ThrowsAsync<InvalidMemberDataException>(() =>
            service.UpdateAsync(created.MemberId, request));
        Assert.Equal(400, error.Status);

        db.ChangeTracker.Clear();
        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal(created.HeightCm, row.HeightCm);
        Assert.Equal(1, row.DataRevision);
    }

    [Fact]
    public async Task Update_after_final_decision_appears_blocks_medical_path_without_admin()
    {
        // يحاكي سباق الإصدار: القرار يظهر قبل/عند بداية التحديث بعد القفل (InMemory no-op للقفل).
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "FD-RACE"));
        await SeedFinalDecisionAsync(db, created.MemberId, CenterA);

        var request = ToUpdate(created, fullName: "سباق");
        request.HeightCm = (created.HeightCm ?? 170m) + 5m;

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.UpdateAsync(created.MemberId, request));

        db.ChangeTracker.Clear();
        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal(created.FullName, row.FullName);
        Assert.Equal(created.HeightCm, row.HeightCm);
        Assert.Equal(1, row.DataRevision);
    }

    [Fact]
    public async Task Update_rolls_back_member_when_audit_save_fails()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var created = await CreateService(db, CenterA, allowCreate: true, allowView: true)
            .CreateAsync(ValidCreate(recruitment: "FD-RB"));
        await SeedFinalDecisionAsync(db, created.MemberId, CenterA);

        var failingUow = new Mock<IUnitOfWork>();
        failingUow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoopTransaction());
        failingUow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit persist failed"));

        var service = CreateService(
            db,
            CenterA,
            allowCreate: true,
            allowView: true,
            allowEditAfterFinalDecision: true,
            unitOfWork: failingUow.Object);

        var request = ToUpdate(created, fullName: "يجب ألا يُحفظ");
        request.EditReason = "سبب صالح";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(created.MemberId, request));

        db.ChangeTracker.Clear();
        var row = await db.Members.AsNoTracking().SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal(created.FullName, row.FullName);
        Assert.Equal(1, row.DataRevision);
        Assert.Empty(await db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == MemberService.MemberUpdateAfterFinalDecisionEventType)
            .ToListAsync());
    }

    [Fact]
    public async Task Update_and_issue_both_invoke_member_row_lock()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);

        var members = new Mock<IMemberRepository>();
        members.Setup(m => m.LockMemberForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        members.Setup(m => m.GetByIdInCenterAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Member?)null);

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Members.CreateUpdate,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CenterA);
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Clinical.FinalDecision,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CenterA);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoopTransaction());

        var config = new TypeAdapterConfig();
        new MemberProfile().Register(config);
        var memberService = new MemberService(
            members.Object,
            Mock.Of<IMemberImportBatchRepository>(),
            new MedicalCenterRepository(db),
            new ReferenceDataRepository(db),
            new ClinicWorkspaceRepository(db),
            new AuditLogRepository(db),
            Mock.Of<ICrossCenterAccessService>(),
            uow.Object,
            new Mapper(config),
            gate.Object,
            new TestCurrentUser(),
            new ExportService());

        var memberId = Guid.NewGuid();
        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            memberService.UpdateAsync(memberId, ToUpdate(new MemberDetailsDto
            {
                MemberId = memberId,
                FullName = "س",
                MotherName = "أ",
                BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-20),
                MaritalStatusId = 1,
                Occupation = "موظف",
                RecruitmentNumber = "L1",
                RecruitmentCenterId = RecruitmentCenterId,
                HasTattoo = false,
                FileNumber = 1,
                QueueNumber = 1,
                QueueDate = DateOnly.FromDateTime(DateTime.UtcNow),
                CurrentMedicalCenterId = CenterA,
                CreatedAt = DateTimeOffset.UtcNow,
                DataRevision = 1,
                ExportStatus = "UNEXPORTED"
            })));

        members.Verify(m => m.LockMemberForUpdateAsync(memberId, It.IsAny<CancellationToken>()), Times.Once);

        var postgres = new Mock<IPostgresSessionGuard>();
        postgres.SetupGet(p => p.DefersFinalDecisionHistoryToDatabase).Returns(false);
        var fdService = new Application.Services.Supervisor.FinalDecisionService(
            gate.Object,
            new TestCurrentUser(),
            members.Object,
            new ClinicWorkspaceRepository(db),
            Mock.Of<ICenterClinicRepository>(),
            new ReferenceDataRepository(db),
            new MedicalCenterRepository(db),
            Mock.Of<IMemberArchiveRepository>(),
            new AuditLogRepository(db),
            uow.Object,
            postgres.Object);

        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            fdService.IssueAsync(memberId, new Application.Dtos.Supervisor.IssueFinalDecisionRequest
            {
                ResultStatusId = 1
            }));

        members.Verify(m => m.LockMemberForUpdateAsync(memberId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static async Task<Guid> SeedFinalDecisionAsync(AppDbContext db, Guid memberId, Guid centerId)
    {
        if (!await db.ResultStatuses.AnyAsync())
        {
            db.ResultStatuses.AddRange(
                new ResultStatus { StatusId = 1, Code = "ACCEPTED", NameAr = "مقبول", IsActive = true },
                new ResultStatus { StatusId = 2, Code = "REJECTED", NameAr = "مرفوض", IsActive = true },
                new ResultStatus { StatusId = 3, Code = "DEFERRED", NameAr = "مؤجل", IsActive = true });
        }

        var decisionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = decisionId,
            MemberId = memberId,
            CenterId = centerId,
            SupervisorUserId = "actor-1",
            ResultStatusId = 1,
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return decisionId;
    }

    [Fact]
    public async Task Update_rejects_invalid_birth_date_using_create_rules()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await service.CreateAsync(ValidCreate());

        var request = ToUpdate(created);
        request.BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10);

        var error = await Assert.ThrowsAsync<InvalidMemberDataException>(() =>
            service.UpdateAsync(created.MemberId, request));
        Assert.Equal(400, error.Status);
        Assert.Equal(1, (await db.Members.SingleAsync(m => m.MemberId == created.MemberId)).DataRevision);
    }

    [Fact]
    public async Task Update_rejects_duplicate_recruitment_number()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var first = await service.CreateAsync(ValidCreate(recruitment: "DUP-U-1"));
        var second = await service.CreateAsync(ValidCreate(recruitment: "DUP-U-2", fullName: "منتسب ثانٍ"));

        var error = await Assert.ThrowsAsync<MemberRecruitmentNumberExistsException>(() =>
            service.UpdateAsync(second.MemberId, ToUpdate(second, recruitment: "dup-u-1")));

        Assert.Equal(400, error.Status);
        Assert.Equal("MEMBER_RECRUITMENT_NUMBER_EXISTS", error.ErrorCode);
        Assert.Equal("DUP-U-2", (await db.Members.SingleAsync(m => m.MemberId == second.MemberId)).RecruitmentNumber);
        Assert.Equal(1, (await db.Members.SingleAsync(m => m.MemberId == first.MemberId)).DataRevision);
    }

    [Fact]
    public async Task GetById_in_other_center_is_not_found()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var createService = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await createService.CreateAsync(ValidCreate());

        var viewB = CreateService(db, CenterB, allowCreate: false, allowView: true);
        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            viewB.GetByIdAsync(created.MemberId));
    }

    [Fact]
    public async Task GetReceipt_returns_pdf_and_writes_audit_without_changing_numbers()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true, allowPrintReceipt: true);
        var created = await service.CreateAsync(ValidCreate());

        var pdf = await service.GetReceiptPdfAsync(created.MemberId);

        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.Equal($"member-receipt-{created.FileNumber}.pdf", pdf.FileName);
        Assert.True(pdf.FileContents.Length > 100);
        Assert.Equal(0x25, pdf.FileContents[0]); // '%'
        Assert.Equal((byte)'P', pdf.FileContents[1]);
        Assert.Equal((byte)'D', pdf.FileContents[2]);
        Assert.Equal((byte)'F', pdf.FileContents[3]);

        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal(created.FileNumber, row.FileNumber);
        Assert.Equal(created.QueueNumber, row.QueueNumber);
        Assert.Equal(1, row.DataRevision);

        var audit = await db.AuditEvents.SingleAsync(e => e.EventType == "MemberReceiptPrint");
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal(nameof(Member), audit.EntityType);
        Assert.Equal(created.MemberId.ToString(), audit.EntityId);
        Assert.Equal(CenterA, audit.CenterId);
        using var values = System.Text.Json.JsonDocument.Parse(audit.NewValues!);
        Assert.Equal(created.FileNumber, values.RootElement.GetProperty("FileNumber").GetInt64());
        Assert.Equal(created.QueueNumber, values.RootElement.GetProperty("QueueNumber").GetInt32());
    }

    [Fact]
    public async Task GetReceipt_reprint_keeps_same_file_and_queue_numbers()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true, allowPrintReceipt: true);
        var created = await service.CreateAsync(ValidCreate());

        var first = await service.GetReceiptPdfAsync(created.MemberId);
        var second = await service.GetReceiptPdfAsync(created.MemberId);

        Assert.True(first.FileContents.Length > 0);
        Assert.True(second.FileContents.Length > 0);
        Assert.Equal(first.FileName, second.FileName);

        var row = await db.Members.SingleAsync(m => m.MemberId == created.MemberId);
        Assert.Equal(created.FileNumber, row.FileNumber);
        Assert.Equal(created.QueueNumber, row.QueueNumber);
        Assert.Equal(1, row.DataRevision);

        var audits = await db.AuditEvents.Where(e => e.EventType == "MemberReceiptPrint").ToListAsync();
        Assert.Equal(2, audits.Count);
        foreach (var audit in audits)
        {
            using var values = System.Text.Json.JsonDocument.Parse(audit.NewValues!);
            Assert.Equal(created.FileNumber, values.RootElement.GetProperty("FileNumber").GetInt64());
            Assert.Equal(created.QueueNumber, values.RootElement.GetProperty("QueueNumber").GetInt32());
        }
    }

    [Fact]
    public async Task GetReceipt_from_other_center_is_not_found()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var createService = CreateService(db, CenterA, allowCreate: true, allowView: true, allowPrintReceipt: true);
        var created = await createService.CreateAsync(ValidCreate());

        var printB = CreateService(db, CenterB, allowCreate: false, allowView: false, allowPrintReceipt: true);
        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            printB.GetReceiptPdfAsync(created.MemberId));

        Assert.Empty(await db.AuditEvents.Where(e => e.EventType == "MemberReceiptPrint").ToListAsync());
    }

    [Fact]
    public async Task GetReceipt_missing_member_is_not_found()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: false, allowView: false, allowPrintReceipt: true);

        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            service.GetReceiptPdfAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetReceipt_without_permission_is_forbidden()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var createService = CreateService(db, CenterA, allowCreate: true, allowView: true, allowPrintReceipt: true);
        var created = await createService.CreateAsync(ValidCreate());

        var denied = CreateService(db, CenterA, allowCreate: false, allowView: false, allowPrintReceipt: false);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            denied.GetReceiptPdfAsync(created.MemberId));

        Assert.Empty(await db.AuditEvents.Where(e => e.EventType == "MemberReceiptPrint").ToListAsync());
    }

    [Fact]
    public async Task List_scopes_to_active_center()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);

        var serviceA = CreateService(db, CenterA, allowCreate: true, allowView: true);
        await serviceA.CreateAsync(ValidCreate(recruitment: "A-1", fullName: "في أ"));

        var serviceB = CreateService(db, CenterB, allowCreate: true, allowView: true);
        await serviceB.CreateAsync(ValidCreate(recruitment: "B-1", fullName: "في ب"));

        var listA = await serviceA.ListAsync(new MemberListQuery());
        Assert.Equal(1, listA.TotalCount);
        Assert.Equal("في أ", Assert.Single(listA.Items).FullName);

        var listB = await serviceB.ListAsync(new MemberListQuery());
        Assert.Equal(1, listB.TotalCount);
        Assert.Equal("في ب", Assert.Single(listB.Items).FullName);
    }

    [Fact]
    public async Task Create_without_permission_is_forbidden()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: false, allowView: false);

        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.CreateAsync(ValidCreate()));

        Assert.Equal(403, error.Status);
        Assert.Equal(0, await db.Members.CountAsync());
    }

    [Fact]
    public async Task Create_rejects_client_center_mismatch_via_gate()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Members.CreateUpdate,
                CenterB,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenPermissionException());

        var service = CreateService(db, gate.Object);
        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.CreateAsync(ValidCreate(), clientCenterId: CenterB));

        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task Create_rejects_duplicate_recruitment_number()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        await service.CreateAsync(ValidCreate(recruitment: "DUP-1"));
        var error = await Assert.ThrowsAsync<MemberRecruitmentNumberExistsException>(() =>
            service.CreateAsync(ValidCreate(recruitment: "dup-1", fullName: "آخر")));

        Assert.Equal(400, error.Status);
        Assert.Equal(1, await db.Members.CountAsync());
    }

    [Fact]
    public async Task Create_rejects_age_outside_active_validation_rule()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var request = ValidCreate();
        request.BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10);

        var error = await Assert.ThrowsAsync<InvalidMemberDataException>(() =>
            service.CreateAsync(request));
        Assert.Equal(400, error.Status);
        Assert.Contains("الحد الأدنى", error.Message);
    }

    [Fact]
    public async Task Create_accepts_age_when_validation_rule_is_inactive()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var rule = await db.ValidationRules.SingleAsync();
        rule.IsActive = false;
        await db.SaveChangesAsync();
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var request = ValidCreate(recruitment: "AGE-OFF");
        request.BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10);

        var created = await service.CreateAsync(request);
        Assert.Equal(request.BirthDate, created.BirthDate);
    }

    [Fact]
    public async Task List_rejects_from_after_to()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: false, allowView: true);

        var error = await Assert.ThrowsAsync<InvalidMemberDataException>(() =>
            service.ListAsync(new MemberListQuery
            {
                FromDate = new DateOnly(2026, 10, 20),
                ToDate = new DateOnly(2026, 10, 10)
            }));
        Assert.Equal(400, error.Status);
    }

    [Fact]
    public async Task List_searches_by_file_name_and_recruitment_separately()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var a = await service.CreateAsync(ValidCreate(recruitment: "SRCH-A", fullName: "أحمد علي"));
        await service.CreateAsync(ValidCreate(recruitment: "SRCH-B", fullName: "سارة محمد"));

        var byFile = await service.ListAsync(new MemberListQuery { FileNumber = a.FileNumber });
        Assert.Equal(1, byFile.TotalCount);
        Assert.Equal(a.MemberId, Assert.Single(byFile.Items).MemberId);

        var byName = await service.ListAsync(new MemberListQuery { FullName = "سارة" });
        Assert.Equal(1, byName.TotalCount);
        Assert.Equal("سارة محمد", Assert.Single(byName.Items).FullName);

        var byRecruitment = await service.ListAsync(new MemberListQuery { RecruitmentNumber = "srch-a" });
        Assert.Equal(1, byRecruitment.TotalCount);
        Assert.Equal("SRCH-A", Assert.Single(byRecruitment.Items).RecruitmentNumber);
    }

    [Fact]
    public async Task List_barcode_maps_to_file_number_and_rejects_invalid()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "BC-1"));

        var byBarcode = await service.ListAsync(new MemberListQuery { Barcode = created.FileNumber.ToString() });
        Assert.Equal(1, byBarcode.TotalCount);
        Assert.Equal(created.MemberId, Assert.Single(byBarcode.Items).MemberId);

        var error = await Assert.ThrowsAsync<InvalidMemberDataException>(() =>
            service.ListAsync(new MemberListQuery { Barcode = "NOT-A-NUMBER" }));
        Assert.Equal(400, error.Status);
        Assert.Contains("باركود", error.Message);
    }

    [Fact]
    public async Task List_pagination_returns_total_and_empty_page()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        for (var i = 0; i < 3; i++)
            await service.CreateAsync(ValidCreate(recruitment: $"PG-{i}", fullName: $"منتسب {i}"));

        var page1 = await service.ListAsync(new MemberListQuery { PageNumber = 1, PageSize = 2 });
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);

        var page2 = await service.ListAsync(new MemberListQuery { PageNumber = 2, PageSize = 2 });
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(1, page2.Items.Count);

        var empty = await service.ListAsync(new MemberListQuery { FullName = "لا يوجد", PageNumber = 1, PageSize = 10 });
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task List_date_range_is_inclusive_on_both_ends()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var early = await service.CreateAsync(ValidCreate(recruitment: "DT-1", fullName: "مبكر"));
        var late = await service.CreateAsync(ValidCreate(recruitment: "DT-2", fullName: "متأخر"));

        var earlyRow = await db.Members.SingleAsync(m => m.MemberId == early.MemberId);
        var lateRow = await db.Members.SingleAsync(m => m.MemberId == late.MemberId);
        earlyRow.CreatedAt = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);
        lateRow.CreatedAt = new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();

        var range = await service.ListAsync(new MemberListQuery
        {
            FromDate = new DateOnly(2026, 10, 10),
            ToDate = new DateOnly(2026, 10, 12)
        });
        Assert.Equal(2, range.TotalCount);

        var single = await service.ListAsync(new MemberListQuery { Date = new DateOnly(2026, 10, 12) });
        Assert.Equal(1, single.TotalCount);
        Assert.Equal(late.MemberId, Assert.Single(single.Items).MemberId);
    }

    [Fact]
    public async Task ListCentral_returns_cross_center_and_records_access()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);

        var localA = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var localB = CreateService(db, CenterB, allowCreate: true, allowView: true);
        await localA.CreateAsync(ValidCreate(recruitment: "CX-A", fullName: "مركزي أ"));
        await localB.CreateAsync(ValidCreate(recruitment: "CX-B", fullName: "مركزي ب"));

        var central = CreateService(db, CenterA, allowCreate: false, allowView: false, allowViewCentral: true);
        var list = await central.ListCentralAsync(
            new MemberListQuery(),
            accessReason: "مراجعة مركزية",
            targetCenterId: null);

        Assert.Equal(2, list.TotalCount);
        var accesses = await db.CrossCenterAccesses.ToListAsync();
        Assert.Equal(2, accesses.Count); // two distinct target centers on the page
        Assert.All(accesses, a => Assert.Equal("Member", a.DataType));
        Assert.All(accesses, a => Assert.Equal("مراجعة مركزية", a.AccessReason));
        Assert.All(accesses, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.QueryCriteria));
            using var doc = JsonDocument.Parse(a.QueryCriteria!);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.True(doc.RootElement.TryGetProperty("pageNumber", out _));
            Assert.True(doc.RootElement.TryGetProperty("pageSize", out _));
        });
        Assert.Equal(2, await db.AuditEvents.CountAsync(e => e.EventType == "VIEW"));
    }

    [Fact]
    public async Task ListCentral_without_permission_is_forbidden()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: false, allowView: true, allowViewCentral: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ListCentralAsync(new MemberListQuery(), accessReason: "سبب"));
    }

    [Fact]
    public async Task GetByIdCentral_records_access_and_includes_notes_and_transfer()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var local = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await local.CreateAsync(ValidCreate(recruitment: "NT-1"));

        var clinicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        db.Clinics.Add(new Clinic
        {
            ClinicId = clinicId,
            Code = "EYE",
            NameAr = "عيون",
            SortOrder = 1,
            IsActive = true
        });
        db.MemberGeneralNotes.Add(new MemberGeneralNote
        {
            NoteId = Guid.NewGuid(),
            MemberId = created.MemberId,
            CenterId = CenterA,
            NoteText = "ملاحظة هامة",
            AuthorUserId = "actor-1",
            AuthorClinicId = clinicId,
            IsActive = true,
            WrittenAt = DateTimeOffset.UtcNow,
            UpdatedByUserId = "actor-1",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.MemberTransfers.Add(new MemberTransfer
        {
            TransferId = Guid.NewGuid(),
            MemberId = created.MemberId,
            FromCenterId = CenterB,
            ToCenterId = CenterA,
            FromMemberCenterIdentityId = Guid.NewGuid(),
            ToMemberCenterIdentityId = Guid.NewGuid(),
            TransferReason = "نقل اختبار",
            ExecutedByUserId = "actor-1",
            ExecutedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var central = CreateService(db, CenterA, allowCreate: false, allowView: false, allowViewCentral: true);
        var details = await central.GetByIdCentralAsync(created.MemberId, accessReason: "اطلاع ملف");

        Assert.True(details.HasGeneralNotesAlert);
        Assert.Equal("ملاحظة هامة", Assert.Single(details.GeneralNotes).NoteText);
        Assert.NotNull(details.LastTransfer);
        Assert.Equal(CenterB, details.LastTransfer!.FromCenterId);
        Assert.Equal("نقل اختبار", details.LastTransfer.TransferReason);

        var access = Assert.Single(await db.CrossCenterAccesses.ToListAsync());
        Assert.Equal(CenterA, access.TargetCenterId);
        using (var doc = JsonDocument.Parse(access.QueryCriteria!))
        {
            Assert.Equal(created.MemberId.ToString("D"), doc.RootElement.GetProperty("memberId").GetString());
        }

        Assert.Equal(created.MemberId.ToString(), await db.AuditEvents
            .Where(e => e.EventType == "VIEW")
            .Select(e => e.EntityId)
            .SingleAsync());
    }

    [Fact]
    public async Task GetById_without_notes_has_no_alert()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "NO-NOTE"));

        var details = await service.GetByIdAsync(created.MemberId);
        Assert.False(details.HasGeneralNotesAlert);
        Assert.Empty(details.GeneralNotes);
        Assert.Null(details.LastTransfer);
        Assert.False(details.HasFinalDecision);
    }

    [Fact]
    public async Task GetById_sets_has_final_decision_true_when_decision_exists()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await service.CreateAsync(ValidCreate(recruitment: "HAS-FD"));
        Assert.False(created.HasFinalDecision);

        await SeedFinalDecisionAsync(db, created.MemberId, CenterA);

        var details = await service.GetByIdAsync(created.MemberId);
        Assert.True(details.HasFinalDecision);
    }

    [Fact]
    public async Task Bmi_is_null_when_height_or_weight_missing()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var request = ValidCreate();
        request.HeightCm = 170;
        request.WeightKg = null;

        var created = await service.CreateAsync(request);
        Assert.Null(created.Bmi);
        Assert.Null(created.BmiCategoryNameAr);
    }

    [Fact]
    public void MemberBmi_calculate_and_classify()
    {
        Assert.Null(MemberBmi.Calculate(null, 70));
        Assert.Equal(22.8m, MemberBmi.Calculate(170, 66));

        var categories = new[]
        {
            new BmiCategory { CategoryId = 1, Code = "N", NameAr = "طبيعي", MinBmi = 18.5m, MaxBmi = 25m, SortOrder = 2, IsActive = true },
            new BmiCategory { CategoryId = 2, Code = "U", NameAr = "نقص وزن", MinBmi = 0m, MaxBmi = 18.5m, SortOrder = 1, IsActive = true }
        };

        Assert.Equal("طبيعي", MemberBmi.Classify(22.8m, categories));
        Assert.Equal("نقص وزن", MemberBmi.Classify(17m, categories));
        Assert.Null(MemberBmi.Classify(null, categories));
    }

    [Fact]
    public async Task Allocate_on_inmemory_skips_advisory_locks_and_returns_next_numbers()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var repo = new MemberRepository(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var (file1, queue1) = await repo.AllocateFileAndQueueNumbersAsync(CenterA, today);
        Assert.Equal(1, file1);
        Assert.Equal(1, queue1);

        db.Members.Add(new Member
        {
            MemberId = Guid.NewGuid(),
            FileNumber = file1,
            RecruitmentNumber = "ALLOC-1",
            FullName = "مخصص",
            MotherName = "أم",
            BirthDate = today.AddYears(-20),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentCenterId = RecruitmentCenterId,
            CurrentMedicalCenterId = CenterA,
            QueueMedicalCenterId = CenterA,
            QueueDate = today,
            QueueNumber = queue1,
            CreatedByUserId = "actor-1",
            UpdatedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var (file2, queue2) = await repo.AllocateFileAndQueueNumbersAsync(CenterA, today);
        Assert.Equal(2, file2);
        Assert.Equal(2, queue2);
    }

    [Fact]
    public async Task Allocate_without_save_advances_from_tracked_pending_members()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var repo = new MemberRepository(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var (file1, queue1) = await repo.AllocateFileAndQueueNumbersAsync(CenterA, today);
        db.Members.Add(new Member
        {
            MemberId = Guid.NewGuid(),
            FileNumber = file1,
            RecruitmentNumber = "ALLOC-PENDING-1",
            FullName = "أول معلّق",
            MotherName = "أم",
            BirthDate = today.AddYears(-20),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentCenterId = RecruitmentCenterId,
            CurrentMedicalCenterId = CenterA,
            QueueMedicalCenterId = CenterA,
            QueueDate = today,
            QueueNumber = queue1,
            CreatedByUserId = "actor-1",
            UpdatedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var (file2, queue2) = await repo.AllocateFileAndQueueNumbersAsync(CenterA, today);
        Assert.Equal(file1 + 1, file2);
        Assert.Equal(queue1 + 1, queue2);

        db.Members.Add(new Member
        {
            MemberId = Guid.NewGuid(),
            FileNumber = file2,
            RecruitmentNumber = "ALLOC-PENDING-2",
            FullName = "ثاني معلّق",
            MotherName = "أم",
            BirthDate = today.AddYears(-21),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentCenterId = RecruitmentCenterId,
            CurrentMedicalCenterId = CenterA,
            QueueMedicalCenterId = CenterA,
            QueueDate = today,
            QueueNumber = queue2,
            CreatedByUserId = "actor-1",
            UpdatedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        var saved = await db.Members.OrderBy(m => m.FileNumber).ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.Equal(new[] { file1, file2 }, saved.Select(m => m.FileNumber).ToArray());
        Assert.Equal(new[] { queue1, queue2 }, saved.Select(m => m.QueueNumber).ToArray());
    }

    [Fact]
    public void Validation_rejects_bad_blood_pressure_format()
    {
        var error = Assert.Throws<InvalidMemberDataException>(() =>
            MemberFieldValidation.ValidateOptionalMeasurements(
                null, null, null, "120-80", null, Array.Empty<ValidationRule>()));
        Assert.Contains("ضغط الدم", error.Message);
    }

    [Fact]
    public void Validation_applies_active_validation_rule_bounds()
    {
        var rules = new[]
        {
            new ValidationRule
            {
                ValidationRuleId = Guid.NewGuid(),
                Code = "H1",
                NameAr = "طول",
                TargetEntity = "Member",
                TargetField = "HeightCm",
                MinValue = 100,
                MaxValue = 250,
                VersionNo = 1,
                ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
                IsActive = true
            }
        };

        var error = Assert.Throws<InvalidMemberDataException>(() =>
            MemberFieldValidation.ValidateOptionalMeasurements(
                null, 50, null, null, null, rules));
        Assert.Contains("الطول", error.Message);
    }

    private static CreateMemberRequest ValidCreate(
        string recruitment = "RN-100",
        string fullName = "منتسب اختبار")
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

    private static UpdateMemberRequest ToUpdate(
        MemberDetailsDto source,
        string? fullName = null,
        string? recruitment = null)
        => new()
        {
            FullName = fullName ?? source.FullName,
            MotherName = source.MotherName,
            BirthDate = source.BirthDate,
            MaritalStatusId = source.MaritalStatusId,
            Occupation = source.Occupation,
            RecruitmentNumber = recruitment ?? source.RecruitmentNumber,
            RecruitmentCenterId = source.RecruitmentCenterId,
            BloodTypeId = source.BloodTypeId,
            BloodSugarMgDl = source.BloodSugarMgDl,
            HeightCm = source.HeightCm,
            WeightKg = source.WeightKg,
            BloodPressure = source.BloodPressure,
            PulseBpm = source.PulseBpm,
            HasTattoo = source.HasTattoo,
            DistinguishingMarks = source.DistinguishingMarks
        };

    internal static MemberService CreateService(
        AppDbContext db,
        Guid activeCenter,
        bool allowCreate,
        bool allowView,
        bool allowPrintReceipt = false,
        bool allowImport = false,
        bool allowExport = false,
        bool allowViewCentral = false,
        bool allowEditAfterFinalDecision = false,
        IUnitOfWork? unitOfWork = null,
        IAuditLogRepository? audit = null)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.Has(MilitaryHealthPermissions.Members.EditAfterFinalDecision))
            .Returns(allowEditAfterFinalDecision);

        if (allowCreate)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.CreateUpdate,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeCenter);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.CreateUpdate,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        if (allowView)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.View,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeCenter);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.View,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        if (allowViewCentral)
        {
            gate.Setup(g => g.EnsureCentralAsync(
                    MilitaryHealthPermissions.Members.ViewCentral,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        else
        {
            gate.Setup(g => g.EnsureCentralAsync(
                    MilitaryHealthPermissions.Members.ViewCentral,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        if (allowPrintReceipt)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.PrintReceipt,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeCenter);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.PrintReceipt,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        if (allowImport)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.Import,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeCenter);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.Import,
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
                .ReturnsAsync(activeCenter);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.Export,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        return CreateService(db, gate.Object, unitOfWork, audit);
    }

    private static MemberService CreateService(
        AppDbContext db,
        IPermissionGate gate,
        IUnitOfWork? unitOfWork = null,
        IAuditLogRepository? audit = null)
    {
        var config = new TypeAdapterConfig();
        new MemberProfile().Register(config);
        var mapper = new Mapper(config);
        var currentUser = new TestCurrentUser();
        var crossCenter = new Application.Services.Audit.CrossCenterAccessService(
            new CrossCenterAccessRepository(db),
            currentUser,
            new TestCenter(null, central: true),
            new TestUnitOfWork(db));

        return new MemberService(
            new MemberRepository(db),
            new MemberImportBatchRepository(db),
            new MedicalCenterRepository(db),
            new ReferenceDataRepository(db),
            new ClinicWorkspaceRepository(db),
            audit ?? new AuditLogRepository(db),
            crossCenter,
            unitOfWork ?? new TestUnitOfWork(db),
            mapper,
            gate,
            currentUser,
            new ExportService());
    }

    internal static async Task SeedReferencesAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new User
        {
            Id = "actor-1",
            UserName = "actor",
            NormalizedUserName = "ACTOR",
            Email = "actor@test.local",
            NormalizedEmail = "ACTOR@TEST.LOCAL",
            SecurityStamp = Guid.NewGuid().ToString(),
            IsActive = true
        });
        db.MedicalCenters.AddRange(
            new MedicalCenter
            {
                CenterId = CenterA,
                Code = "A",
                NameAr = "مركز أ",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new MedicalCenter
            {
                CenterId = CenterB,
                Code = "B",
                NameAr = "مركز ب",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        db.MaritalStatuses.Add(new MaritalStatus
        {
            MaritalStatusId = 1,
            Code = "S",
            NameAr = "أعزب",
            SortOrder = 1,
            IsSystem = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BloodTypes.Add(new BloodType
        {
            BloodTypeId = 1,
            Code = "O+",
            NameAr = "O+",
            SortOrder = 1,
            IsSystem = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.RecruitmentCenters.Add(new RecruitmentCenter
        {
            RecruitmentCenterId = RecruitmentCenterId,
            Code = "RC1",
            NameAr = "تجنيد 1",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ValidationRules.Add(ReceptionReferenceSeed.CreateAgeRule(now));
        db.BmiCategories.AddRange(
            new BmiCategory
            {
                CategoryId = 1,
                Code = "UNDER",
                NameAr = "نقص وزن",
                MinBmi = 0,
                MaxBmi = 18.5m,
                SortOrder = 1,
                IsActive = true
            },
            new BmiCategory
            {
                CategoryId = 2,
                Code = "NORMAL",
                NameAr = "طبيعي",
                MinBmi = 18.5m,
                MaxBmi = 25m,
                SortOrder = 2,
                IsActive = true
            },
            new BmiCategory
            {
                CategoryId = 3,
                Code = "OVER",
                NameAr = "زيادة وزن",
                MinBmi = 25m,
                MaxBmi = 30m,
                SortOrder = 3,
                IsActive = true
            });
        await db.SaveChangesAsync();
    }

    internal static AppDbContext CreateDb()
    {
        var user = new TestCurrentUser();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, user, new TestCenter(null, true), new AuditInterceptor(user));
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
