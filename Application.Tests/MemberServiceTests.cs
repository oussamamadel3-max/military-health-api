using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Mappings;
using Application.Services.Members;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
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
            new MedicalCenterRepository(db),
            new ReferenceDataRepository(db),
            audit.Object,
            uow.Object,
            new Mapper(config),
            gate.Object,
            new TestCurrentUser());

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
    public async Task Create_rejects_age_outside_temporary_br004()
    {
        await using var db = CreateDb();
        await SeedReferencesAsync(db);
        var service = CreateService(db, CenterA, allowCreate: true, allowView: true);

        var request = ValidCreate();
        request.BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10);

        var error = await Assert.ThrowsAsync<InvalidMemberDataException>(() =>
            service.CreateAsync(request));
        Assert.Equal(400, error.Status);
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

        var (file1, queue1) = await repo.AllocateFileAndQueueNumbersAsync(CenterA, DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(1, file1);
        Assert.Equal(1, queue1);

        db.Members.Add(new Member
        {
            MemberId = Guid.NewGuid(),
            FileNumber = file1,
            RecruitmentNumber = "ALLOC-1",
            FullName = "مخصص",
            MotherName = "أم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-20),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentCenterId = RecruitmentCenterId,
            CurrentMedicalCenterId = CenterA,
            QueueMedicalCenterId = CenterA,
            QueueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            QueueNumber = queue1,
            CreatedByUserId = "actor-1",
            UpdatedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var (file2, queue2) = await repo.AllocateFileAndQueueNumbersAsync(CenterA, DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.Equal(2, file2);
        Assert.Equal(2, queue2);
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

    private static MemberService CreateService(
        AppDbContext db,
        Guid activeCenter,
        bool allowCreate,
        bool allowView,
        bool allowPrintReceipt = false)
    {
        var gate = new Mock<IPermissionGate>();
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

        return CreateService(db, gate.Object);
    }

    private static MemberService CreateService(AppDbContext db, IPermissionGate gate)
    {
        var config = new TypeAdapterConfig();
        new MemberProfile().Register(config);
        var mapper = new Mapper(config);
        var currentUser = new TestCurrentUser();

        return new MemberService(
            new MemberRepository(db),
            new MedicalCenterRepository(db),
            new ReferenceDataRepository(db),
            new AuditLogRepository(db),
            new TestUnitOfWork(db),
            mapper,
            gate,
            currentUser);
    }

    private static async Task SeedReferencesAsync(AppDbContext db)
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

    private static AppDbContext CreateDb()
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
