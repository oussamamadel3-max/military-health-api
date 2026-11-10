using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace Application.Tests;

/// <summary>
/// تكامل PostgreSQL لقوائم إسنادات المركز. تُتخطى عند غياب <c>MHS_EXPORT_EF_REVIEW_PG</c>
/// أو ملف كلمة مرور المراجع المعزول.
/// </summary>
public class CenterAssignmentListPostgresIntegrationTests
{
    private const string RequiredDatabase = "mhs_export_ef_review";
    private static readonly Guid PgCenterA = Guid.Parse("e1111111-1111-1111-1111-111111111111");
    private static readonly Guid PgCenterB = Guid.Parse("e2222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Postgres_role_and_direct_lists_execute_without_linq_translation_errors()
    {
        var cs = ResolveConnectionString();
        if (cs is null) return;
        await EnsureReviewRoleSchemaAsync(cs);

        await using var db = CreatePostgresContext(cs);
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await EnsureContextActorUserAsync(db);
            await SeedCenterAsync(db, PgCenterA, "PG-A", "مركز اختبار أ");
            var clerkRoleId = await EnsureRoleAsync(db, "ReceptionClerk");
            var permId = await EnsurePermissionAsync(db, "Members.View");

            var actorId = $"pg-actor-{Guid.NewGuid():N}"[..20];
            var targetId = $"pg-target-{Guid.NewGuid():N}"[..20];
            await EnsureUserAsync(db, actorId, "pg.actor");
            await EnsureUserAsync(db, targetId, "pg.target", "هدف");
            await SeedSuperAdminAsync(db, actorId);

            db.UserRoleAssociations.Add(new UserRoleAssociation
            {
                Id = Guid.NewGuid(),
                UserId = targetId,
                RoleId = clerkRoleId,
                CenterId = PgCenterA,
                IsActive = true,
                AssignedByUserId = actorId,
                AssignedAt = DateTimeOffset.UtcNow
            });
            db.UserPermissions.Add(new UserPermission
            {
                Id = Guid.NewGuid(),
                UserId = targetId,
                PermissionId = permId,
                CenterId = PgCenterA,
                IsActive = true,
                GrantedByUserId = actorId,
                GrantedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var service = CreateService(db, actorId, PgCenterA, unrestricted: true);

            var roles = await service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
            {
                CenterId = PgCenterA,
                PageNumber = 1,
                PageSize = 10,
                Q = "pg.target",
                IsActive = true
            });

            var directs = await service.ListCenterDirectPermissionsAsync(new CenterDirectPermissionListQuery
            {
                CenterId = PgCenterA,
                PageNumber = 1,
                PageSize = 10,
                Q = "Members",
                IsActive = true
            });

            Assert.True(roles.TotalCount >= 1);
            Assert.True(directs.TotalCount >= 1);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Postgres_pagination_total_count_and_stable_pages()
    {
        var cs = ResolveConnectionString();
        if (cs is null) return;
        await EnsureReviewRoleSchemaAsync(cs);

        await using var db = CreatePostgresContext(cs);
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await EnsureContextActorUserAsync(db);
            await SeedCenterAsync(db, PgCenterA, "PG-PG", "مركز ترقيم");
            var clerkRoleId = await EnsureRoleAsync(db, "ReceptionClerk");
            var actorId = $"pg-pg-actor-{Guid.NewGuid():N}"[..24];
            await EnsureUserAsync(db, actorId, "pg.paginator");
            await SeedSuperAdminAsync(db, actorId);

            const int total = 12;
            const int pageSize = 5;
            for (var i = 0; i < total; i++)
            {
                var uid = $"pg-u-{i:D3}-{Guid.NewGuid():N}"[..24];
                await EnsureUserAsync(db, uid, $"pg.user{i}");
                db.UserRoleAssociations.Add(new UserRoleAssociation
                {
                    Id = Guid.NewGuid(),
                    UserId = uid,
                    RoleId = clerkRoleId,
                    CenterId = PgCenterA,
                    IsActive = true,
                    AssignedByUserId = actorId,
                    AssignedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
                });
            }
            await db.SaveChangesAsync();

            var service = CreateService(db, actorId, PgCenterA, unrestricted: true);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var pageNum = 1; pageNum <= 3; pageNum++)
            {
                var page = await service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
                {
                    CenterId = PgCenterA,
                    PageNumber = pageNum,
                    PageSize = pageSize,
                    IsActive = true
                });
                if (pageNum == 1)
                    Assert.Equal(total, page.TotalCount);
                foreach (var item in page.Items)
                    Assert.True(keys.Add(item.RowKey), $"duplicate row {item.RowKey} on page {pageNum}");
            }

            Assert.Equal(total, keys.Count);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Postgres_local_manager_rejects_foreign_center_id()
    {
        var cs = ResolveConnectionString();
        if (cs is null) return;
        await EnsureReviewRoleSchemaAsync(cs);

        await using var db = CreatePostgresContext(cs);
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await EnsureContextActorUserAsync(db);
            await SeedCenterAsync(db, PgCenterA, "PG-LOC-A", "مركز محلي أ");
            await SeedCenterAsync(db, PgCenterB, "PG-LOC-B", "مركز محلي ب");
            var managerId = $"pg-mgr-{Guid.NewGuid():N}"[..20];
            await EnsureUserAsync(db, managerId, "pg.manager");
            await EnsureUserCenterAsync(db, managerId, PgCenterA);
            await EnsureLocalManagerRoleAsync(db, managerId, PgCenterA);

            var service = CreateService(db, managerId, PgCenterA, unrestricted: false);

            await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
                service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
                {
                    CenterId = PgCenterB,
                    PageNumber = 1,
                    PageSize = 10
                }));
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Postgres_revoked_doctor_visible_without_user_center_and_without_clinic_explosion()
    {
        var cs = ResolveConnectionString();
        if (cs is null) return;
        await EnsureReviewRoleSchemaAsync(cs);

        await using var db = CreatePostgresContext(cs);
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await EnsureContextActorUserAsync(db);
            await SeedCenterAsync(db, PgCenterA, "PG-HIST", "مركز تاريخي");
            var doctorRoleId = await EnsureRoleAsync(db, "ClinicDoctor");
            var clinicId = Guid.Parse("f3333333-3333-3333-3333-333333333333");
            await EnsureClinicAsync(db, clinicId, "PGC1", "عيادة تاريخ", 1);
            await EnsureCenterClinicAsync(db, PgCenterA, clinicId);

            var actorId = $"pg-hist-act-{Guid.NewGuid():N}"[..24];
            var docId = $"pg-hist-doc-{Guid.NewGuid():N}"[..24];
            await EnsureUserAsync(db, actorId, "pg.hist.actor");
            await EnsureUserAsync(db, docId, "pg.hist.doc");
            await SeedSuperAdminAsync(db, actorId);
            await EnsureUserCenterAsync(db, docId, PgCenterA);
            var inactiveCenterLink = await db.UserCenters.SingleAsync(uc =>
                uc.UserId == docId && uc.CenterId == PgCenterA);
            inactiveCenterLink.IsActive = false;
            inactiveCenterLink.RevokedAt = DateTimeOffset.UtcNow;
            inactiveCenterLink.RevokedByUserId = actorId;
            inactiveCenterLink.RevokeReason = "إنهاء إسناد المركز";
            await db.SaveChangesAsync();

            db.UserRoleAssociations.Add(new UserRoleAssociation
            {
                Id = Guid.NewGuid(),
                UserId = docId,
                RoleId = doctorRoleId,
                CenterId = PgCenterA,
                IsActive = false,
                AssignedByUserId = actorId,
                AssignedAt = DateTimeOffset.UtcNow.AddDays(-30),
                RevokedAt = DateTimeOffset.UtcNow.AddDays(-1),
                RevokedByUserId = actorId,
                RevokeReason = "سحب دور"
            });
            db.UserClinicAssignments.Add(new UserClinicAssignment
            {
                AssignmentId = Guid.NewGuid(),
                UserId = docId,
                CenterId = PgCenterA,
                ClinicId = clinicId,
                IsActive = true,
                AssignedByUserId = actorId,
                AssignedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var service = CreateService(db, actorId, PgCenterA, unrestricted: true);
            var page = await service.ListCenterRoleAssignmentsAsync(new CenterRoleAssignmentListQuery
            {
                CenterId = PgCenterA,
                PageNumber = 1,
                PageSize = 10,
                IsActive = false
            });

            var row = Assert.Single(page.Items.Where(i => i.UserId == docId));
            Assert.Null(row.ClinicId);
            Assert.False(row.IsRoleAssignmentActive);
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Postgres_explain_analyze_role_list_when_opt_in()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MHS_RUN_EXPLAIN"), "1", StringComparison.Ordinal))
            return;

        var cs = ResolveConnectionString();
        if (cs is null) return;
        await EnsureReviewRoleSchemaAsync(cs);

        await using var db = CreatePostgresContext(cs);
        var rows = BuildCenterRoleAssignmentRows(db, PgCenterA);
        _ = await rows.CountAsync();

        var sql = rows
            .OrderByDescending(r => r.RoleAssignmentId)
            .Take(10)
            .ToQueryString();

        try
        {
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) {sql}", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            var plan = new List<string>();
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));

            Assert.NotEmpty(plan);
        }
        catch (PostgresException)
        {
            // EF يصدّر SQL مُعلَّمًا بمعاملات؛ عند فشل التنفيذ المباشر انسخ ToQueryString إلى psql يدويًا للتحليل.
        }
    }

    private static IQueryable<CenterRoleAssignmentRowProbe> BuildCenterRoleAssignmentRows(AppDbContext db, Guid centerId)
    {
        var clinicDoctor = UserClinicAssignmentService.ClinicDoctorRoleName;
        var roles = db.Roles.AsNoTracking();
        var clinics = db.UserClinicAssignments.AsNoTracking();
        var baseQuery = db.UserRoleAssociations.AsNoTracking().Where(ura => ura.CenterId == centerId);

        var nonExploded =
            from ura in baseQuery
            join role in roles on ura.RoleId equals role.Id
            where !(ura.IsActive && role.Name == clinicDoctor)
            select new CenterRoleAssignmentRowProbe { RoleAssignmentId = ura.Id, ClinicAssignmentId = Guid.Empty };

        var exploded =
            from ura in baseQuery
            join role in roles on ura.RoleId equals role.Id
            where ura.IsActive && role.Name == clinicDoctor
            from uca in clinics
                .Where(uca => uca.UserId == ura.UserId && uca.CenterId == centerId && uca.IsActive)
                .DefaultIfEmpty()
            where uca != null
                || !clinics.Any(uca2 =>
                    uca2.UserId == ura.UserId && uca2.CenterId == centerId && uca2.IsActive)
            select new CenterRoleAssignmentRowProbe
            {
                RoleAssignmentId = ura.Id,
                ClinicAssignmentId = uca != null ? uca.AssignmentId : Guid.Empty
            };

        return nonExploded.Concat(exploded);
    }

    private sealed class CenterRoleAssignmentRowProbe
    {
        public Guid RoleAssignmentId { get; init; }
        public Guid ClinicAssignmentId { get; init; }
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

    /// <summary>
    /// قاعدة المراجعة المعزولة قد تتأخر عن آخر migration؛ الاستعلامات التي تربط <c>Roles</c> تتطلب العمود.
    /// </summary>
    private static async Task EnsureReviewRoleSchemaAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var check = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = 'iam' AND table_name = 'Roles' AND column_name = 'PermissionSetVersion'
            """,
            connection);
        if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
            return;

        await using var add = new NpgsqlCommand(
            """ALTER TABLE iam."Roles" ADD COLUMN "PermissionSetVersion" bigint NOT NULL DEFAULT 0""",
            connection);
        await add.ExecuteNonQueryAsync();
    }

    private static AppDbContext CreatePostgresContext(string connectionString)
    {
        var user = new TestCurrentUser();
        user.SetUser("pg-actor", "pg", "127.0.0.1");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        // CENTRAL أثناء البذرة حتى لا يُدرِج التدقيق centerId قبل إنشاء المركز في PostgreSQL.
        return new AppDbContext(options, user, new TestCenter(null, central: true), new AuditInterceptor(user));
    }

    private static UserAssignmentService CreateService(
        AppDbContext db,
        string actorId,
        Guid activeCenterId,
        bool unrestricted)
    {
        var user = new TestCurrentUser();
        user.SetUser(actorId, actorId, "127.0.0.1");

        var users = new UserRepository(db);
        var roles = new RoleRepository(db);
        var centers = new MedicalCenterRepository(db);
        var access = new AccessContextRepository(db);
        var tokens = new RefreshTokenRepository(db);
        var uow = new PostgresTestUnitOfWork(db);

        var manager = CreateUserManager();
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns((string id) => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id));

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureAll(It.IsAny<string>()));
        gate.Setup(g => g.GetPermissions()).Returns(new[] { SystemPermissions.UserManagement.Read });

        return new UserAssignmentService(
            manager.Object,
            users,
            roles,
            centers,
            access,
            tokens,
            uow,
            gate.Object,
            user,
            new TestCenter(activeCenterId, central: false),
            Mock.Of<IAuditLogRepository>());
    }

    private static async Task SeedSuperAdminAsync(AppDbContext db, string actorId)
    {
        var roleId = await EnsureRoleAsync(db, "SuperAdmin");
        if (await db.UserRoleAssociations.AnyAsync(ura =>
                ura.UserId == actorId && ura.RoleId == roleId && ura.IsActive && ura.CenterId == null))
            return;

        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = actorId,
            RoleId = roleId,
            CenterId = null,
            IsActive = true,
            AssignedByUserId = actorId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        foreach (var permissionName in SystemPermissions.UserManagementWrite
            .Concat(SystemPermissions.RoleManagementWrite)
            .Concat(SystemPermissions.PermissionManagementWrite)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var permId = await EnsurePermissionAsync(db, permissionName);
            if (!await db.RolePermissions.AnyAsync(rp =>
                    rp.RoleId == roleId && rp.PermissionId == permId && rp.IsActive))
            {
                db.RolePermissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = roleId,
                    PermissionId = permId,
                    IsActive = true,
                    GrantedByUserId = actorId,
                    GrantedAt = DateTimeOffset.UtcNow
                });
            }
        }
        await db.SaveChangesAsync();
    }

    private static async Task EnsureLocalManagerRoleAsync(AppDbContext db, string userId, Guid centerId)
    {
        var roleId = await EnsureRoleAsync(db, "CenterManager");
        db.UserRoleAssociations.Add(new UserRoleAssociation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = roleId,
            CenterId = centerId,
            IsActive = true,
            AssignedByUserId = userId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureUserCenterAsync(AppDbContext db, string userId, Guid centerId)
    {
        if (await db.UserCenters.AnyAsync(uc => uc.UserId == userId && uc.CenterId == centerId))
            return;

        db.UserCenters.Add(new UserCenter
        {
            UserId = userId,
            CenterId = centerId,
            IsActive = true,
            AssignedByUserId = userId,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static Task EnsureContextActorUserAsync(AppDbContext db) =>
        EnsureUserAsync(db, "pg-actor", "pg.context.actor");

    private static async Task EnsureUserAsync(AppDbContext db, string id, string userName, string? fullName = null)
    {
        if (await db.Users.AnyAsync(u => u.Id == id))
            return;

        db.Users.Add(new User
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            FullName = fullName,
            Email = $"{userName}@pg.test",
            NormalizedEmail = $"{userName}@pg.test".ToUpperInvariant(),
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            CreationDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCenterAsync(AppDbContext db, Guid centerId, string code, string nameAr)
    {
        if (await db.MedicalCenters.AnyAsync(c => c.CenterId == centerId))
            return;

        var now = DateTimeOffset.UtcNow;
        db.MedicalCenters.Add(new MedicalCenter
        {
            CenterId = centerId,
            Code = code,
            NameAr = nameAr,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> EnsureRoleAsync(AppDbContext db, string name)
    {
        var existing = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
        if (existing is not null)
            return existing.Id;

        var role = new Role
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Scope = AssignableRolePolicy.CatalogScopeForName(name),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private static async Task<Guid> EnsurePermissionAsync(AppDbContext db, string name)
    {
        var existing = await db.Permissions.FirstOrDefaultAsync(p => p.Name == name);
        if (existing is not null)
            return existing.Id;

        var perm = new Permission { Id = Guid.NewGuid(), Name = name, IsActive = true };
        db.Permissions.Add(perm);
        await db.SaveChangesAsync();
        return perm.Id;
    }

    private static async Task EnsureClinicAsync(AppDbContext db, Guid clinicId, string code, string nameAr, int sort)
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

    private static async Task EnsureCenterClinicAsync(AppDbContext db, Guid centerId, Guid clinicId)
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

    private static Mock<UserManager<User>> CreateUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(store.Object, null, null, null, null, null, null, null, null);
    }

    private sealed class PostgresTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public PostgresTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            _db.Database.BeginTransactionAsync(cancellationToken);
    }
}
