using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
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

/// <summary>
/// يثبت أن منتسباً واحداً يمكن أن يملك فحصاً أساسياً مستقلاً في كل عيادة
/// دون تداخل تفاصيل التخصصات.
/// </summary>
public class FiveClinicExamIsolationTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClinicInternal = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ClinicSurgery = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ClinicOrtho = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Same_member_keeps_independent_current_exam_per_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAllClinicsAsync(db);
        var memberId = await CreateMemberAsync(db, "FIVE-CLINICS");
        const string doctor = "actor-1";

        await AcquireLockAsync(db, memberId, ClinicEyes, doctor);
        var eye = CreateEye(db, doctor);
        var savedEye = await eye.SaveAsync(memberId, EyeRequest());

        await AcquireLockAsync(db, memberId, ClinicInternal, doctor);
        var internalExam = CreateInternal(db, doctor);
        var savedInternal = await internalExam.SaveAsync(memberId, InternalRequest());

        await AcquireLockAsync(db, memberId, ClinicSurgery, doctor);
        var surgery = CreateSurgery(db, doctor);
        var savedSurgery = await surgery.SaveAsync(memberId, SurgeryRequest());

        await AcquireLockAsync(db, memberId, ClinicOrtho, doctor);
        var ortho = CreateOrtho(db, doctor);
        var savedOrtho = await ortho.SaveAsync(memberId, OrthoRequest());

        await AcquireLockAsync(db, memberId, ClinicEnt, doctor);
        var ent = CreateEnt(db, doctor);
        var savedEnt = await ent.SaveAsync(memberId, EntRequest());

        var examIds = new[]
        {
            savedEye.ExamId, savedInternal.ExamId, savedSurgery.ExamId, savedOrtho.ExamId, savedEnt.ExamId
        };
        Assert.Equal(5, examIds.Distinct().Count());
        Assert.Equal(5, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(1, await db.EyeExamDetails.CountAsync());
        Assert.Equal(1, await db.InternalExamDetails.CountAsync());
        Assert.Equal(1, await db.SurgeryExamDetails.CountAsync());
        Assert.Equal(1, await db.OrthopedicExamDetails.CountAsync());
        Assert.Equal(1, await db.EntExamDetails.CountAsync());

        var gotEye = await eye.GetAsync(memberId, ClinicEyes);
        var gotInternal = await internalExam.GetAsync(memberId, ClinicInternal);
        var gotSurgery = await surgery.GetAsync(memberId, ClinicSurgery);
        var gotOrtho = await ortho.GetAsync(memberId, ClinicOrtho);
        var gotEnt = await ent.GetAsync(memberId, ClinicEnt);

        Assert.Equal("EYE-R", gotEye.VisualAcuityRight);
        Assert.Equal("INT-CV", gotInternal.Cardiovascular);
        Assert.Equal("SUR-GEN", gotSurgery.GeneralSurgery);
        Assert.Equal("ORT-MSK", gotOrtho.Musculoskeletal);
        Assert.Equal("ENT-R", gotEnt.RightEar);

        Assert.Equal(savedEye.ExamId, gotEye.ExamId);
        Assert.Equal(savedInternal.ExamId, gotInternal.ExamId);
        Assert.Equal(savedSurgery.ExamId, gotSurgery.ExamId);
        Assert.Equal(savedOrtho.ExamId, gotOrtho.ExamId);
        Assert.Equal(savedEnt.ExamId, gotEnt.ExamId);

        await AcquireLockAsync(db, memberId, ClinicInternal, doctor);
        var update = InternalRequest();
        update.Cardiovascular = "INT-CV-2";
        var updatedInternal = await internalExam.SaveAsync(memberId, update);

        Assert.Equal(savedInternal.ExamId, updatedInternal.ExamId);
        Assert.Equal(2, updatedInternal.ExamVersionNo);
        Assert.Equal("EYE-R", (await eye.GetAsync(memberId, ClinicEyes)).VisualAcuityRight);
        Assert.Equal("SUR-GEN", (await surgery.GetAsync(memberId, ClinicSurgery)).GeneralSurgery);
        Assert.Equal("ORT-MSK", (await ortho.GetAsync(memberId, ClinicOrtho)).Musculoskeletal);
        Assert.Equal("ENT-R", (await ent.GetAsync(memberId, ClinicEnt)).RightEar);
        Assert.Equal(5, await db.MedicalExams.CountAsync(e => e.MemberId == memberId && e.IsCurrent));
        Assert.Equal(1, await db.InternalExamDetails.CountAsync());
    }

    private static SaveEyeExamRequest EyeRequest()
        => new()
        {
            ClinicId = ClinicEyes,
            VisualAcuityRight = "EYE-R",
            VisualAcuityLeft = "EYE-L",
            ColorTestResult = "EYE-COLOR",
            ResultStatusId = 1,
            Refractions =
            [
                new EyeRefractionInputDto
                {
                    EyeSide = EyeSides.Right,
                    RefractionType = "SPHERE",
                    InputFormat = EyeRefractionInputFormats.Decimal,
                    DecimalInput = -1m,
                    IsWorst = true
                }
            ]
        };

    private static SaveInternalExamRequest InternalRequest()
        => new()
        {
            ClinicId = ClinicInternal,
            Cardiovascular = "INT-CV",
            Respiratory = "INT-RESP",
            Digestive = "INT-DIG",
            Endocrine = "INT-END",
            Neurological = "INT-NEU",
            Hematology = "INT-HEM",
            Joints = "INT-JOI",
            Renal = "INT-REN",
            Skin = "INT-SKN",
            ResultStatusId = 1
        };

    private static SaveSurgeryExamRequest SurgeryRequest()
        => new()
        {
            ClinicId = ClinicSurgery,
            GeneralSurgery = "SUR-GEN",
            Urology = "SUR-URO",
            VascularThoracic = "SUR-VAS",
            ResultStatusId = 1
        };

    private static SaveOrthopedicExamRequest OrthoRequest()
        => new()
        {
            ClinicId = ClinicOrtho,
            Musculoskeletal = "ORT-MSK",
            Neurosurgery = "ORT-NS",
            ResultStatusId = 1
        };

    private static SaveEntExamRequest EntRequest()
        => new()
        {
            ClinicId = ClinicEnt,
            RightEar = "ENT-R",
            LeftEar = "ENT-L",
            TympanicMembrane = "ENT-TM",
            HearingTest = "ENT-H",
            WhisperTest = "ENT-W",
            NoseExam = "ENT-N",
            MouthExam = "ENT-M",
            ResultStatusId = 1
        };

    private static EyeExamService CreateEye(AppDbContext db, string userId) =>
        new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new IsolationUnitOfWork(db),
            Gate(),
            User(userId));

    private static InternalExamService CreateInternal(AppDbContext db, string userId) =>
        new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new IsolationUnitOfWork(db),
            Gate(),
            User(userId));

    private static SurgeryExamService CreateSurgery(AppDbContext db, string userId) =>
        new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new IsolationUnitOfWork(db),
            Gate(),
            User(userId));

    private static OrthopedicExamService CreateOrtho(AppDbContext db, string userId) =>
        new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new IsolationUnitOfWork(db),
            Gate(),
            User(userId));

    private static EntExamService CreateEnt(AppDbContext db, string userId) =>
        new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new IsolationUnitOfWork(db),
            Gate(),
            User(userId));

    private static IPermissionGate Gate()
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CenterA);
        return gate.Object;
    }

    private static TestCurrentUser User(string userId)
    {
        var current = new TestCurrentUser();
        current.SetUser(userId, userId, "127.0.0.1");
        return current;
    }

    private static async Task SeedAllClinicsAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedClinicAsync(db, ClinicInternal, "INTERNAL", "الباطنة", 3);
        await SeedClinicAsync(db, ClinicSurgery, "SURGERY", "الجراحة", 4);
        await SeedClinicAsync(db, ClinicOrtho, "ORTHOPEDIC", "العظمية", 5);
        foreach (var clinicId in new[] { ClinicEyes, ClinicEnt, ClinicInternal, ClinicSurgery, ClinicOrtho })
        {
            await SeedCenterClinicAsync(db, CenterA, clinicId);
            await SeedDoctorAssignmentAsync(db, "actor-1", CenterA, clinicId);
        }

        db.ResultStatuses.AddRange(
            new ResultStatus { StatusId = 1, Code = "ACCEPTED", NameAr = "مقبول", IsActive = true },
            new ResultStatus { StatusId = 2, Code = "REJECTED", NameAr = "مرفوض", IsActive = true },
            new ResultStatus { StatusId = 3, Code = "DEFERRED", NameAr = "مؤجل", IsActive = true });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب خمس عيادات",
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

    private static async Task AcquireLockAsync(AppDbContext db, Guid memberId, Guid clinicId, string userId)
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
            CenterId = CenterA,
            ClinicId = clinicId,
            LockedByUserId = userId,
            LockedAt = now,
            ExpiresAt = now.AddMinutes(30)
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedClinicAsync(AppDbContext db, Guid clinicId, string code, string nameAr, int sort)
    {
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
        {
            db.Users.Add(new User
            {
                Id = userId,
                UserName = userId,
                NormalizedUserName = userId.ToUpperInvariant(),
                Email = $"{userId}@test.local",
                NormalizedEmail = $"{userId.ToUpperInvariant()}@TEST.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString(),
                IsActive = true,
                FullName = userId
            });
            await db.SaveChangesAsync();
        }

        db.UserClinicAssignments.Add(new UserClinicAssignment
        {
            AssignmentId = Guid.NewGuid(),
            UserId = userId,
            CenterId = centerId,
            ClinicId = clinicId,
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = userId
        });
        await db.SaveChangesAsync();
    }

    private sealed class IsolationUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public IsolationUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new IsolationNoopTransaction());
    }

    private sealed class IsolationNoopTransaction : IDbContextTransaction
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
