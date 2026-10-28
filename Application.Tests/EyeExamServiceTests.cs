using Application.Common.Permissions;
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

public class EyeExamServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Save_accepted_persists_eye_detail_fraction_and_decimal()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-OK");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var saved = await service.SaveAsync(memberId, AcceptedEyeRequest());

        Assert.Equal((short)1, saved.ResultStatusId);
        Assert.Equal(1, saved.ExamVersionNo);
        Assert.Equal("6/6", saved.VisualAcuityRight);
        Assert.Equal("6/9", saved.VisualAcuityLeft);
        Assert.Equal("طبيعي", saved.ColorTestResult);
        Assert.Equal(2, saved.Refractions.Count);

        var fraction = Assert.Single(saved.Refractions, r => r.InputFormat == EyeRefractionInputFormats.Fraction);
        Assert.Equal(1m, fraction.Numerator);
        Assert.Equal(2m, fraction.Denominator);
        Assert.Null(fraction.DecimalInput);
        Assert.Equal(0.5m, fraction.CalculatedValue);
        Assert.True(fraction.IsWorst);

        var decimalRow = Assert.Single(saved.Refractions, r => r.InputFormat == EyeRefractionInputFormats.Decimal);
        Assert.Equal(-1.25m, decimalRow.DecimalInput);
        Assert.Equal(-1.25m, decimalRow.CalculatedValue);
        Assert.True(decimalRow.IsWorst);

        Assert.Equal(1, await db.EyeExamDetails.CountAsync());
        Assert.Equal(2, await db.EyeRefractionDetails.CountAsync());
        Assert.Equal(0, await db.ClinicExamEditLocks.CountAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == EyeExamService.EyeExamSavedEventType && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Save_rejects_zero_denominator()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-DEN0");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEyeRequest();
        request.Refractions =
        [
            new EyeRefractionInputDto
            {
                EyeSide = EyeSides.Right,
                RefractionType = "SPHERE",
                InputFormat = EyeRefractionInputFormats.Fraction,
                Numerator = 1,
                Denominator = 0,
                IsWorst = true
            }
        ];

        var error = await Assert.ThrowsAsync<InvalidEyeExamDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Contains("مقام", error.Message);
        Assert.Equal(0, await db.EyeExamDetails.CountAsync());
        Assert.Equal(1, await db.ClinicExamEditLocks.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_non_eye_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEnt);
        var memberId = await CreateMemberAsync(db, "EYE-ENT");
        await AcquireLockAsync(db, memberId, ClinicEnt, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEyeRequest();
        request.ClinicId = ClinicEnt;

        await Assert.ThrowsAsync<InvalidEyeExamDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Equal(0, await db.EyeExamDetails.CountAsync());
    }

    [Fact]
    public async Task Save_rejected_without_reason_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-REJ");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEyeRequest();
        request.ResultStatusId = 2;
        request.RejectionReason = null;

        await Assert.ThrowsAsync<InvalidClinicAssessmentDataException>(() =>
            service.SaveAsync(memberId, request));

        Assert.Equal(0, await db.EyeExamDetails.CountAsync());
    }

    [Fact]
    public async Task Save_deferred_without_action_is_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-DEF");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEyeRequest();
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
        var memberId = await CreateMemberAsync(db, "EYE-DEF-OK");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEyeRequest();
        request.ResultStatusId = 3;
        request.DeferReason = "يحتاج تقرير";
        request.ActionTypeId = 1;
        request.FollowupNote = "متابعة";
        request.ActionDescription = "طلب تقرير طبي";

        var saved = await service.SaveAsync(memberId, request);

        Assert.Equal((short)3, saved.ResultStatusId);
        Assert.NotNull(saved.DeferredActionId);
        Assert.Equal("REQUESTED", Assert.Single(await db.DeferredActions.ToListAsync()).Status);
    }

    [Fact]
    public async Task Save_updates_version_and_refraction_in_place()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-UPD");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedEyeRequest());
        var rightId = Assert.Single(first.Refractions, r => r.EyeSide == EyeSides.Right).RefractionDetailId;

        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var update = AcceptedEyeRequest();
        update.VisualAcuityRight = "6/12";
        update.ResultStatusId = 2;
        update.RejectionReason = "ضعف بصر";
        update.Refractions =
        [
            new EyeRefractionInputDto
            {
                RefractionDetailId = rightId,
                EyeSide = EyeSides.Right,
                RefractionType = "SPHERE",
                InputFormat = EyeRefractionInputFormats.Fraction,
                Numerator = 1,
                Denominator = 3,
                IsWorst = true
            },
            new EyeRefractionInputDto
            {
                RefractionDetailId = Assert.Single(first.Refractions, r => r.EyeSide == EyeSides.Left).RefractionDetailId,
                EyeSide = EyeSides.Left,
                RefractionType = "CYLINDER",
                InputFormat = EyeRefractionInputFormats.Decimal,
                DecimalInput = -2.00m,
                IsWorst = true
            }
        ];

        var second = await service.SaveAsync(memberId, update);

        Assert.Equal(first.ExamId, second.ExamId);
        Assert.Equal(2, second.ExamVersionNo);
        Assert.Equal("6/12", second.VisualAcuityRight);
        Assert.Equal((short)2, second.ResultStatusId);
        Assert.Equal(1m, Assert.Single(second.Refractions, r => r.EyeSide == EyeSides.Right).Numerator);
        Assert.Equal(3m, Assert.Single(second.Refractions, r => r.EyeSide == EyeSides.Right).Denominator);
        Assert.Equal(2, await db.EyeRefractionDetails.CountAsync());
    }

    [Fact]
    public async Task Save_rejects_omitting_existing_refraction_id()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-OMIT");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedEyeRequest());
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");

        var update = AcceptedEyeRequest();
        update.Refractions =
        [
            new EyeRefractionInputDto
            {
                RefractionDetailId = Assert.Single(first.Refractions, r => r.EyeSide == EyeSides.Right).RefractionDetailId,
                EyeSide = EyeSides.Right,
                RefractionType = "SPHERE",
                InputFormat = EyeRefractionInputFormats.Fraction,
                Numerator = 1,
                Denominator = 2,
                IsWorst = true
            }
        ];

        var error = await Assert.ThrowsAsync<InvalidEyeExamDataException>(() =>
            service.SaveAsync(memberId, update));

        Assert.Contains("لا يمكن حذف", error.Message);
        Assert.Equal(2, await db.EyeRefractionDetails.CountAsync());
    }

    [Fact]
    public async Task Save_without_lock_is_conflict()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-NOLK");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<ClinicExamEditLockConflictException>(() =>
            service.SaveAsync(memberId, AcceptedEyeRequest()));

        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == EyeExamService.ExamConflictEventType && e.Result == "FAILURE"));
    }

    [Fact]
    public async Task Save_rejects_missing_worst_per_eye()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-WORST");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var request = AcceptedEyeRequest();
        request.Refractions =
        [
            new EyeRefractionInputDto
            {
                EyeSide = EyeSides.Right,
                RefractionType = "SPHERE",
                InputFormat = EyeRefractionInputFormats.Decimal,
                DecimalInput = -1m,
                IsWorst = false
            }
        ];

        await Assert.ThrowsAsync<InvalidEyeExamDataException>(() =>
            service.SaveAsync(memberId, request));
    }

    [Fact]
    public async Task Get_returns_saved_exam_as_entered()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-GET");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");
        await service.SaveAsync(memberId, AcceptedEyeRequest());

        var got = await service.GetAsync(memberId, ClinicEyes);

        Assert.Equal("6/6", got.VisualAcuityRight);
        Assert.Equal(EyeRefractionInputFormats.Fraction,
            Assert.Single(got.Refractions, r => r.EyeSide == EyeSides.Right).InputFormat);
        Assert.Equal((short)1, got.ResultStatusId);
    }

    [Fact]
    public async Task Get_missing_exam_is_not_found()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-MISS");
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<EyeExamNotFoundException>(() =>
            service.GetAsync(memberId, ClinicEyes));
    }

    [Fact]
    public async Task List_returns_current_eye_exams_with_assessment_columns()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-LIST1");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");
        await service.SaveAsync(memberId, AcceptedEyeRequest());

        var page = await service.ListAsync(new EyeExamListQuery { ClinicId = ClinicEyes });

        var item = Assert.Single(page.Items);
        Assert.Equal(memberId, item.MemberId);
        Assert.Equal("6/6", item.VisualAcuityRight);
        Assert.Equal("6/9", item.VisualAcuityLeft);
        Assert.Equal("طبيعي", item.ColorTestResult);
        Assert.Equal((short)1, item.ResultStatusId);
        Assert.Equal("مقبول", item.ResultStatusNameAr);
        Assert.Equal("actor-1", item.ExaminerUserId);
    }

    [Fact]
    public async Task List_filters_by_file_number_and_paginates()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var firstId = await CreateMemberAsync(db, "EYE-P1");
        var secondId = await CreateMemberAsync(db, "EYE-P2");
        var service = CreateService(db, CenterA, "actor-1");

        await AcquireLockAsync(db, firstId, ClinicEyes, "actor-1");
        await service.SaveAsync(firstId, AcceptedEyeRequest());
        await AcquireLockAsync(db, secondId, ClinicEyes, "actor-1");
        await service.SaveAsync(secondId, AcceptedEyeRequest());

        var firstMember = await db.Members.AsNoTracking().SingleAsync(m => m.MemberId == firstId);
        var filtered = await service.ListAsync(new EyeExamListQuery
        {
            ClinicId = ClinicEyes,
            FileNumber = firstMember.FileNumber
        });
        Assert.Equal(firstId, Assert.Single(filtered.Items).MemberId);

        var page = await service.ListAsync(new EyeExamListQuery
        {
            ClinicId = ClinicEyes,
            PageNumber = 1,
            PageSize = 1
        });
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.True(page.HasNextPage);
    }

    [Fact]
    public async Task List_rejects_non_eye_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEnt);
        var service = CreateService(db, CenterA, "actor-1");

        await Assert.ThrowsAsync<InvalidEyeExamDataException>(() =>
            service.ListAsync(new EyeExamListQuery { ClinicId = ClinicEnt }));
    }

    [Fact]
    public async Task List_hides_exams_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await SeedCenterClinicAsync(db, CenterB, ClinicEyes);
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterB, ClinicEyes);

        var membersB = MemberServiceTests.CreateService(db, CenterB, allowCreate: true, allowView: true);
        var other = await membersB.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب مركز آخر",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = "EYE-XB",
            RecruitmentCenterId = RecruitmentCenterId,
            BloodTypeId = 1,
            HeightCm = 170,
            WeightKg = 66,
            BloodPressure = "120/80",
            PulseBpm = 72,
            HasTattoo = false
        });

        var serviceB = CreateService(db, CenterB, "actor-1");
        await AcquireLockAsync(db, other.MemberId, ClinicEyes, "actor-1", CenterB);
        await serviceB.SaveAsync(other.MemberId, AcceptedEyeRequest());

        var localId = await CreateMemberAsync(db, "EYE-XA");
        var serviceA = CreateService(db, CenterA, "actor-1");
        await AcquireLockAsync(db, localId, ClinicEyes, "actor-1");
        await serviceA.SaveAsync(localId, AcceptedEyeRequest());

        var page = await serviceA.ListAsync(new EyeExamListQuery { ClinicId = ClinicEyes });

        Assert.Equal(localId, Assert.Single(page.Items).MemberId);
    }

    [Fact]
    public async Task List_empty_returns_no_items()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, "actor-1");

        var page = await service.ListAsync(new EyeExamListQuery { ClinicId = ClinicEyes });

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Save_second_pass_updates_same_current_exam()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, "EYE-DUP");
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");
        var service = CreateService(db, CenterA, "actor-1");

        var first = await service.SaveAsync(memberId, AcceptedEyeRequest());
        await AcquireLockAsync(db, memberId, ClinicEyes, "actor-1");

        var secondRequest = AcceptedEyeRequest();
        secondRequest.Refractions = first.Refractions.Select(r => new EyeRefractionInputDto
        {
            RefractionDetailId = r.RefractionDetailId,
            EyeSide = r.EyeSide,
            RefractionType = r.RefractionType,
            InputFormat = r.InputFormat,
            Numerator = r.Numerator,
            Denominator = r.Denominator,
            DecimalInput = r.DecimalInput,
            IsWorst = r.IsWorst
        }).ToList();

        var second = await service.SaveAsync(memberId, secondRequest);

        Assert.Equal(first.ExamId, second.ExamId);
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(1, await db.EyeExamDetails.CountAsync());
    }

    private static EyeExamService CreateService(AppDbContext db, Guid centerId, string userId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser(userId, userId, "127.0.0.1");

        return new EyeExamService(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new EyeExamTestUnitOfWork(db),
            gate.Object,
            currentUser);
    }

    private static SaveEyeExamRequest AcceptedEyeRequest()
        => new()
        {
            ClinicId = ClinicEyes,
            VisualAcuityRight = "6/6",
            VisualAcuityLeft = "6/9",
            ColorTestResult = "طبيعي",
            OtherEyeDiseases = "لا يوجد",
            ResultStatusId = 1,
            Refractions =
            [
                new EyeRefractionInputDto
                {
                    EyeSide = EyeSides.Right,
                    RefractionType = "SPHERE",
                    InputFormat = EyeRefractionInputFormats.Fraction,
                    Numerator = 1,
                    Denominator = 2,
                    IsWorst = true
                },
                new EyeRefractionInputDto
                {
                    EyeSide = EyeSides.Left,
                    RefractionType = "CYLINDER",
                    InputFormat = EyeRefractionInputFormats.Decimal,
                    DecimalInput = -1.25m,
                    IsWorst = true
                }
            ]
        };

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);
        await SeedCenterClinicAsync(db, CenterA, ClinicEnt);
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
        await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, ClinicEyes);
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب عيون",
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

    private static async Task AcquireLockAsync(
        AppDbContext db,
        Guid memberId,
        Guid clinicId,
        string userId,
        Guid? centerId = null)
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
            CenterId = centerId ?? CenterA,
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

    private sealed class EyeExamTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public EyeExamTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new EyeExamNoopTransaction());
    }

    private sealed class EyeExamNoopTransaction : IDbContextTransaction
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
