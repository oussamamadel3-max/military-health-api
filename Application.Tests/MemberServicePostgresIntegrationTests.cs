using Application.Common.Permissions;
using Application.Interfaces.Services;
using Application.Dtos.Members;
using Application.Interfaces;
using Application.Mappings;
using Application.Services;
using Application.Services.Members;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;

namespace Application.Tests;

/// <summary>
/// اختبارات تكامل حقيقية لـ <see cref="MemberService.UpdateAsync"/> على PostgreSQL المعزول
/// (<c>mhs_export_ef_review</c> فقط). تُتخطى عند غياب متغير الاتصال.
/// </summary>
public class MemberServicePostgresIntegrationTests
{
    private const string RequiredDatabase = "mhs_export_ef_review";

    private static readonly Guid PgMedicalCenterId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PgRecruitmentCenterId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Postgres_prepareMemberRevision_allowlist_is_present()
    {
        var connectionString = ResolveConnectionString();
        if (connectionString is null)
            return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT pg_get_functiondef(p.oid)
            FROM pg_proc p
            JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE n.nspname = 'patient' AND p.proname = 'prepareMemberRevision'
            """,
            connection);
        var def = (string?)await cmd.ExecuteScalarAsync();
        Assert.False(string.IsNullOrWhiteSpace(def));
        Assert.DoesNotContain("to_jsonb(NEW)", def!, StringComparison.Ordinal);
        Assert.Contains("NEW.\"fullName\"", def!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAsync_on_postgres_exported_member_material_change_persists_expected_state()
    {
        var connectionString = ResolveConnectionString();
        if (connectionString is null)
            return;
        await using var db = CreatePostgresContext(connectionString);
        await EnsureMinimalSeedAsync(db);

        var service = CreateMemberService(db, PgMedicalCenterId);
        var recruitment = $"PG-MAT-{Guid.NewGuid():N}"[..20];
        var created = await service.CreateAsync(ValidCreate(recruitment));

        var before = await db.Members.AsNoTracking().SingleAsync(m => m.MemberId == created.MemberId);
        var revisionBefore = before.DataRevision;

        await SetExportedStateAsync(db, created.MemberId, revisionBefore);
        db.ChangeTracker.Clear();

        var request = ToUpdate(created, fullName: "اسم بعد تصدير PG");
        await service.UpdateAsync(created.MemberId, request);

        db.ChangeTracker.Clear();
        var row = await db.Members.AsNoTracking().SingleAsync(m => m.MemberId == created.MemberId);

        Assert.Equal("UNEXPORTED", row.ExportStatus);
        Assert.Equal(revisionBefore + 1, row.DataRevision);
        Assert.Equal(revisionBefore, row.ReviewedRevision);
        Assert.Equal(revisionBefore, row.LastExportedRevision);
        Assert.Null(row.LastExportedAt);
        Assert.Equal("اسم بعد تصدير PG", row.FullName);

        Assert.True(new MemberRepository(db).MaterialMemberRevisionManagedByDatabase());
    }

    [Fact]
    public async Task UpdateAsync_on_postgres_exported_member_without_material_change_preserves_export()
    {
        var connectionString = ResolveConnectionString();
        if (connectionString is null)
            return;
        await using var db = CreatePostgresContext(connectionString);
        await EnsureMinimalSeedAsync(db);

        var service = CreateMemberService(db, PgMedicalCenterId);
        var recruitment = $"PG-NOOP-{Guid.NewGuid():N}"[..20];
        var created = await service.CreateAsync(ValidCreate(recruitment));

        var before = await db.Members.AsNoTracking().SingleAsync(m => m.MemberId == created.MemberId);
        var revisionBefore = before.DataRevision;
        var exportedAt = DateTimeOffset.UtcNow.AddDays(-3);

        await SetExportedStateAsync(db, created.MemberId, revisionBefore, exportedAt);
        db.ChangeTracker.Clear();

        var request = ToUpdate(created, fullName: created.FullName);
        await service.UpdateAsync(created.MemberId, request);

        db.ChangeTracker.Clear();
        var row = await db.Members.AsNoTracking().SingleAsync(m => m.MemberId == created.MemberId);

        Assert.Equal("EXPORTED", row.ExportStatus);
        Assert.Equal(revisionBefore, row.DataRevision);
        Assert.Equal(revisionBefore, row.LastExportedRevision);
        Assert.NotNull(row.LastExportedAt);
        Assert.Equal(exportedAt.UtcDateTime, row.LastExportedAt.Value.UtcDateTime, precision: TimeSpan.FromSeconds(1));
    }

    private static string? ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("MHS_EXPORT_EF_REVIEW_PG");
        if (!string.IsNullOrWhiteSpace(cs))
            return cs.Trim();

        var pwdPath = Path.Combine(Path.GetTempPath(), "mhs_review_tester_pwd.txt");
        if (!File.Exists(pwdPath))
            return null;

        var pwd = File.ReadAllText(pwdPath).Trim();
        return $"Host=127.0.0.1;Port=5432;Database={RequiredDatabase};Username=mhs_review_tester;Password={pwd}";
    }

    private static string RequireExportReviewConnection()
    {
        var cs = ResolveConnectionString()
            ?? throw new InvalidOperationException(
                "Set MHS_EXPORT_EF_REVIEW_PG or create %TEMP%\\mhs_review_tester_pwd.txt for isolated review DB.");

        var builder = new NpgsqlConnectionStringBuilder(cs);
        if (!string.Equals(builder.Database, RequiredDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing database '{builder.Database}'. Only '{RequiredDatabase}' is allowed.");
        }

        return cs;
    }

    private static AppDbContext CreatePostgresContext(string connectionString)
    {
        var user = new TestCurrentUser();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppDbContext(options, user, new TestCenter(PgMedicalCenterId, central: false), new AuditInterceptor(user));
    }

    private static async Task EnsureMinimalSeedAsync(AppDbContext db)
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

        if (!await db.MedicalCenters.AnyAsync(c => c.CenterId == PgMedicalCenterId))
        {
            db.MedicalCenters.Add(new MedicalCenter
            {
                CenterId = PgMedicalCenterId,
                Code = "TST",
                NameAr = "مركز اختبار",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.RecruitmentCenters.AnyAsync(r => r.RecruitmentCenterId == PgRecruitmentCenterId))
        {
            db.RecruitmentCenters.Add(new RecruitmentCenter
            {
                RecruitmentCenterId = PgRecruitmentCenterId,
                Code = "RC",
                NameAr = "مركز تجنيد",
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

        if (!await db.ValidationRules.AnyAsync(r => r.TargetEntity.ToLower() == "member" && r.IsActive))
            db.ValidationRules.Add(ReceptionReferenceSeed.CreateAgeRule(now));

        if (!await db.BmiCategories.AnyAsync())
        {
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
                });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();
    }

    private static async Task SetExportedStateAsync(
        AppDbContext db,
        Guid memberId,
        long revision,
        DateTimeOffset? exportedAt = null)
    {
        var at = exportedAt ?? DateTimeOffset.UtcNow.AddDays(-1);
        var member = await db.Members.SingleAsync(m => m.MemberId == memberId);
        member.DataRevision = revision;
        member.ReviewedRevision = revision;
        member.LastExportedRevision = revision;
        member.ExportStatus = "EXPORTED";
        member.LastExportedAt = at;
        member.ReviewedByUserId = "actor-1";
        member.ReviewedAt = at;
        await db.SaveChangesAsync();
    }

    private static MemberService CreateMemberService(AppDbContext db, Guid activeCenter)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Members.CreateUpdate,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeCenter);
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Members.View,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeCenter);

        var config = new TypeAdapterConfig();
        new MemberProfile().Register(config);
        var mapper = new Mapper(config);
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
            mapper,
            gate.Object,
            currentUser,
            new ExportService());
    }

    private static CreateMemberRequest ValidCreate(string recruitment) => new()
    {
        FullName = "منتسب تكامل PG",
        MotherName = "الأم",
        BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25),
        MaritalStatusId = 1,
        Occupation = "موظف",
        RecruitmentNumber = recruitment,
        RecruitmentCenterId = PgRecruitmentCenterId,
        BloodTypeId = 1,
        HeightCm = 170,
        WeightKg = 66,
        BloodPressure = "120/80",
        PulseBpm = 72,
        HasTattoo = false
    };

    private static UpdateMemberRequest ToUpdate(MemberDetailsDto source, string? fullName = null) => new()
    {
        FullName = fullName ?? source.FullName,
        MotherName = source.MotherName,
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
}
