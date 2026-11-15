using Application.Common.Options;
using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Mappings;
using Application.Services;
using Application.Services.Members;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Domain.Enums;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;

namespace Application.Tests;

/// <summary>
/// Phase C.2: مصفوفة إنفاذ MotherName على PostgreSQL معزول (<c>mhs_fp_c2_review</c> فقط).
/// لا تُتخطى عند الفشل — غياب الاتصال يفشل الاختبار صراحةً.
/// </summary>
public class FieldPermissionsPostgresC2Tests
{
    private const string RequiredDatabase = "mhs_fp_c2_review";
    private static readonly Guid CenterId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly object DbInitLock = new();
    private static bool _dbReady;
    private static string? _connectionString;
    private static string? _initError;

    public FieldPermissionsPostgresC2Tests()
    {
        EnsureIsolatedDatabase();
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC2")]
    public async Task Pg_C2_read_semantics_None_Read_Write_ReadWrite_and_default_deny()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var memberId = await InsertMemberAsync(db, "أم مخزّنة C2");

        await AssertMaskedAsync(db, memberId, FieldAccessType.None, expectVisible: false, "أم مخزّنة C2");
        await AssertMaskedAsync(db, memberId, FieldAccessType.Write, expectVisible: false, "أم مخزّنة C2");
        await AssertMaskedAsync(db, memberId, FieldAccessType.Read, expectVisible: true, "أم مخزّنة C2");
        await AssertMaskedAsync(db, memberId, FieldAccessType.ReadWrite, expectVisible: true, "أم مخزّنة C2");

        // Default Deny: لا إسناد → None
        var users = new Mock<IUserPermissionService>();
        users.Setup(u => u.GetFieldAccessAsync(It.IsAny<string>(), "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FieldAccessType.None);
        var details = await ViewWithAccessAsync(db, memberId, users.Object, enabled: true);
        Assert.True(string.IsNullOrEmpty(details.MotherName));
        Assert.Equal("أم مخزّنة C2", await StoredMotherAsync(db, memberId));

        await CleanupMemberAsync(db, memberId);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC2")]
    public async Task Pg_C2_create_update_respect_write_and_do_not_mutate_on_mask()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        var denied = CreateService(db, CreateGuard(enabled: true, FieldAccessType.Read));
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            denied.CreateAsync(ValidCreate("C2-DENY-" + Guid.NewGuid().ToString("N")[..8])));

        var writer = CreateService(db, CreateGuard(enabled: true, FieldAccessType.Write));
        var created = await writer.CreateAsync(ValidCreate("C2-WR-" + Guid.NewGuid().ToString("N")[..8]));
        Assert.True(string.IsNullOrEmpty(created.MotherName));
        Assert.Equal("أم الاختبار", await StoredMotherAsync(db, created.MemberId));

        var reader = CreateService(db, CreateGuard(enabled: true, FieldAccessType.Read));
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            reader.UpdateAsync(created.MemberId, ToUpdate(created, motherName: "أم معدّلة")));

        Assert.Equal("أم الاختبار", await StoredMotherAsync(db, created.MemberId));

        var same = await reader.UpdateAsync(created.MemberId, ToUpdate(created, motherName: "أم الاختبار", fullName: "اسم جديد"));
        Assert.Equal("اسم جديد", same.FullName);
        Assert.True(string.IsNullOrEmpty(same.MotherName));
        Assert.Equal("أم الاختبار", await StoredMotherAsync(db, created.MemberId));

        var rw = CreateService(db, CreateGuard(enabled: true, FieldAccessType.ReadWrite));
        var updated = await rw.UpdateAsync(created.MemberId, ToUpdate(created, motherName: "أم بعد الكتابة", fullName: "اسم جديد"));
        Assert.Equal("أم بعد الكتابة", updated.MotherName);
        Assert.Equal("أم بعد الكتابة", await StoredMotherAsync(db, created.MemberId));

        await CleanupMemberAsync(db, created.MemberId);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC2")]
    public async Task Pg_C2_enforcement_off_preserves_previous_visibility()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var memberId = await InsertMemberAsync(db, "أم ظاهرة");

        var users = new Mock<IUserPermissionService>(MockBehavior.Strict);
        var service = CreateService(db, CreateGuard(enabled: false, users.Object));
        var details = await service.GetByIdAsync(memberId);
        Assert.Equal("أم ظاهرة", details.MotherName);
        users.VerifyNoOtherCalls();

        await CleanupMemberAsync(db, memberId);
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC2")]
    public async Task Pg_C2_import_commit_requires_write_when_enforcement_on()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        var batchId = Guid.NewGuid();
        var row = new MemberImportValidRow
        {
            RowNumber = 1,
            FullName = "مستورد C2",
            MotherName = "أم الاستيراد",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-20),
            MaritalStatusId = 1,
            Occupation = "طالب",
            RecruitmentNumber = "C2-IMP-" + Guid.NewGuid().ToString("N")[..8],
            RecruitmentCenterId = RecruitmentCenterId,
            BloodTypeId = 1,
            HeightCm = 170,
            WeightKg = 65
        };

        db.MemberImportBatches.Add(new Domain.Entities.Patient.MemberImportBatch
        {
            ImportBatchId = batchId,
            CenterId = CenterId,
            CreatedByUserId = "actor-1",
            Status = "Previewed",
            FileName = "c2.xlsx",
            ContentHash = Guid.NewGuid().ToString("N"),
            ValidCount = 1,
            DuplicateCount = 0,
            InvalidCount = 0,
            ValidRowsJson = System.Text.Json.JsonSerializer.Serialize(new[] { row }),
            ErrorsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var denied = CreateService(db, CreateGuard(enabled: true, FieldAccessType.Read));
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            denied.CommitImportAsync(batchId));

        Assert.False(await db.Members.AnyAsync(m => m.RecruitmentNumber == row.RecruitmentNumber));
    }

    [Fact]
    [Trait("Feature", "FieldPermissions-PhaseC2")]
    public async Task Pg_C2_user_override_and_role_union_via_permission_service()
    {
        // دلالات الدمج على مسار الخدمة الحقيقي — لا تُتخطى.
        var users = new Mock<IUserRepository>();
        users.Setup(u => u.GetUserFieldAccessAsync("u1", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FieldAccessType.None);
        users.Setup(u => u.GetActiveRoleIdsAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "r1" });

        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetRoleFieldAccessTypesAsync(
                It.IsAny<IEnumerable<string>>(), "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FieldAccessType> { FieldAccessType.ReadWrite });

        var access = new Mock<IAccessContextRepository>();
        var center = new Mock<ICurrentCenterService>();
        center.SetupGet(c => c.IsCentralContext).Returns(true);
        var svc = new Application.Services.AuthServices.UserPermissionService(
            users.Object, roles.Object, access.Object, center.Object);

        Assert.Equal(FieldAccessType.None,
            await svc.GetFieldAccessAsync("u1", "Member", "MotherName"));

        users.Setup(u => u.GetUserFieldAccessAsync("u2", "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldAccessType?)null);
        users.Setup(u => u.GetActiveRoleIdsAsync("u2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "r1", "r2" });
        roles.Setup(r => r.GetRoleFieldAccessTypesAsync(
                It.IsAny<IEnumerable<string>>(), "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FieldAccessType> { FieldAccessType.Read, FieldAccessType.Write });

        Assert.Equal(FieldAccessType.ReadWrite,
            await svc.GetFieldAccessAsync("u2", "Member", "MotherName"));
    }

    #region Bootstrap / helpers

    private static void EnsureIsolatedDatabase()
    {
        lock (DbInitLock)
        {
            if (_dbReady || _initError is not null)
            {
                if (_initError is not null)
                    throw new InvalidOperationException(_initError);
                return;
            }

            try
            {
                var adminCs = ResolveAdminConnectionString();
                var testCs = ResolveTestConnectionString();

                using (var admin = new NpgsqlConnection(adminCs))
                {
                    admin.Open();
                    using var exists = new NpgsqlCommand(
                        "SELECT 1 FROM pg_database WHERE datname = @n", admin);
                    exists.Parameters.AddWithValue("n", RequiredDatabase);
                    var found = exists.ExecuteScalar() is not null;
                    if (!found)
                    {
                        using var create = new NpgsqlCommand(
                            $"CREATE DATABASE \"{RequiredDatabase}\"", admin);
                        create.ExecuteNonQuery();
                    }
                }

                using var db = CreateDb(testCs);
                db.Database.Migrate();
                _connectionString = testCs;
                _dbReady = true;
            }
            catch (Exception ex)
            {
                _initError =
                    "C.2 blocked: تعذر تجهيز PostgreSQL المعزول mhs_fp_c2_review. " +
                    "وفّر اتصالاً محلياً (أو MHS_FP_C2_PG / MHS_FP_C2_ADMIN_PG). السبب: " + ex.Message;
                throw new InvalidOperationException(_initError, ex);
            }
        }
    }

    private static string ResolveAdminConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("MHS_FP_C2_ADMIN_PG");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        // قاعدة postgres الإدارية فقط — لا تُستخدم MilitaryHealthDB كهدف اختبار.
        return "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123";
    }

    private static string ResolveTestConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("MHS_FP_C2_PG");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var b = new NpgsqlConnectionStringBuilder(env.Trim());
            if (!string.Equals(b.Database, RequiredDatabase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refusing database '{b.Database}'. Only '{RequiredDatabase}' is allowed for C.2.");
            }

            return b.ConnectionString;
        }

        return $"Host=localhost;Port=5432;Database={RequiredDatabase};Username=postgres;Password=123";
    }

    private static AppDbContext CreateDb(string? cs = null)
    {
        var connection = cs ?? _connectionString
            ?? throw new InvalidOperationException(_initError ?? "C.2 DB not initialized.");
        var user = new TestCurrentUser();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new AppDbContext(options, user, new TestCenter(CenterId, central: false), new AuditInterceptor(user));
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await db.Users.AnyAsync(u => u.Id == "actor-1"))
        {
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
        }

        if (!await db.MedicalCenters.AnyAsync(c => c.CenterId == CenterId))
        {
            db.MedicalCenters.Add(new MedicalCenter
            {
                CenterId = CenterId,
                Code = "C2",
                NameAr = "مركز C2",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.RecruitmentCenters.AnyAsync(r => r.RecruitmentCenterId == RecruitmentCenterId))
        {
            db.RecruitmentCenters.Add(new RecruitmentCenter
            {
                RecruitmentCenterId = RecruitmentCenterId,
                Code = "RC2",
                NameAr = "تجنيد C2",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.MaritalStatuses.AnyAsync(m => m.MaritalStatusId == 1))
        {
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
        }

        if (!await db.BloodTypes.AnyAsync(b => b.BloodTypeId == 1))
        {
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
        }

        if (!await db.BmiCategories.AnyAsync())
        {
            db.BmiCategories.Add(new BmiCategory
            {
                CategoryId = 1,
                Code = "NORMAL",
                NameAr = "طبيعي",
                MinBmi = 18.5m,
                MaxBmi = 25m,
                SortOrder = 1,
                IsActive = true
            });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();
    }

    private static async Task<Guid> InsertMemberAsync(AppDbContext db, string motherName)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        db.Members.Add(new Member
        {
            MemberId = id,
            FileNumber = Math.Abs(BitConverter.ToInt64(id.ToByteArray(), 0) % 900000) + 100000,
            QueueNumber = 1,
            QueueDate = DateOnly.FromDateTime(now.UtcDateTime),
            FullName = "منتسب C2",
            MotherName = motherName,
            Occupation = "موظف",
            RecruitmentNumber = "C2-M-" + Guid.NewGuid().ToString("N")[..10],
            CurrentMedicalCenterId = CenterId,
            QueueMedicalCenterId = CenterId,
            MaritalStatusId = 1,
            BloodTypeId = 1,
            RecruitmentCenterId = RecruitmentCenterId,
            BirthDate = new DateOnly(2000, 1, 1),
            HeightCm = 170,
            WeightKg = 65,
            ExportStatus = "UNEXPORTED",
            DataRevision = 1,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = "actor-1",
            UpdatedByUserId = "actor-1"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task CleanupMemberAsync(AppDbContext db, Guid memberId)
    {
        var row = await db.Members.FirstOrDefaultAsync(m => m.MemberId == memberId);
        if (row is null) return;
        db.Members.Remove(row);
        await db.SaveChangesAsync();
    }

    private static async Task AssertMaskedAsync(
        AppDbContext db,
        Guid memberId,
        FieldAccessType access,
        bool expectVisible,
        string storedValue)
    {
        var details = await ViewWithAccessAsync(
            db, memberId, CreateUserPermissionMock(access).Object, enabled: true);
        if (expectVisible)
            Assert.Equal(storedValue, details.MotherName);
        else
            Assert.True(string.IsNullOrEmpty(details.MotherName));
        Assert.Equal(storedValue, await StoredMotherAsync(db, memberId));
    }

    private static async Task<MemberDetailsDto> ViewWithAccessAsync(
        AppDbContext db,
        Guid memberId,
        IUserPermissionService users,
        bool enabled)
    {
        var service = CreateService(db, CreateGuard(enabled, users));
        return await service.GetByIdAsync(memberId);
    }

    private static async Task<string> StoredMotherAsync(AppDbContext db, Guid memberId)
    {
        db.ChangeTracker.Clear();
        return await db.Members.AsNoTracking()
            .Where(m => m.MemberId == memberId)
            .Select(m => m.MotherName)
            .SingleAsync();
    }

    private static Mock<IUserPermissionService> CreateUserPermissionMock(FieldAccessType access)
    {
        var users = new Mock<IUserPermissionService>();
        users.Setup(u => u.GetFieldAccessAsync(
                It.IsAny<string>(), "Member", "MotherName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(access);
        return users;
    }

    private static IMemberFieldPermissionGuard CreateGuard(bool enabled, FieldAccessType access)
        => CreateGuard(enabled, CreateUserPermissionMock(access).Object);

    private static IMemberFieldPermissionGuard CreateGuard(bool enabled, IUserPermissionService users)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.UserId).Returns("actor-1");
        return new MemberFieldPermissionGuard(
            Microsoft.Extensions.Options.Options.Create(
                new FieldPermissionsOptions { EnforcementEnabled = enabled }),
            users,
            current.Object);
    }

    private static MemberService CreateService(AppDbContext db, IMemberFieldPermissionGuard guard)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CenterId);
        gate.Setup(g => g.Has(It.IsAny<string>())).Returns(false);

        var config = new TypeAdapterConfig();
        new MemberProfile().Register(config);
        var currentUser = new TestCurrentUser();
        var uow = new UnitOfWork(db);
        var crossCenter = new Application.Services.Audit.CrossCenterAccessService(
            new CrossCenterAccessRepository(db),
            currentUser,
            new TestCenter(null, central: true),
            uow);

        return new MemberService(
            new MemberRepository(db),
            new MemberImportBatchRepository(db),
            new MedicalCenterRepository(db),
            new ReferenceDataRepository(db),
            new ClinicWorkspaceRepository(db),
            new AuditLogRepository(db),
            crossCenter,
            uow,
            new Mapper(config),
            gate.Object,
            currentUser,
            new ExportService(),
            fieldPermissions: guard);
    }

    private static CreateMemberRequest ValidCreate(string recruitment) => new()
    {
        FullName = "منتسب C2",
        MotherName = "أم الاختبار",
        BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25),
        MaritalStatusId = 1,
        Occupation = "موظف",
        RecruitmentNumber = recruitment,
        RecruitmentCenterId = RecruitmentCenterId,
        BloodTypeId = 1,
        HeightCm = 170,
        WeightKg = 65
    };

    private static UpdateMemberRequest ToUpdate(
        MemberDetailsDto source,
        string? motherName = null,
        string? fullName = null) => new()
    {
        FullName = fullName ?? source.FullName,
        MotherName = motherName ?? source.MotherName ?? "أم الاختبار",
        BirthDate = source.BirthDate,
        MaritalStatusId = source.MaritalStatusId,
        Occupation = source.Occupation,
        RecruitmentNumber = source.RecruitmentNumber,
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

    #endregion
}
