using Application.Common.Permissions;
using Application.Common.Security;
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
/// تكامل PostgreSQL لاستعلام <c>EffectiveDoctorsCount</c> في <c>GetCenterClinicsAsync</c>.
/// يُتخطى عند غياب <c>MHS_EXPORT_EF_REVIEW_PG</c> أو ملف كلمة مرور قاعدة المراجعة المعزولة.
/// </summary>
public class UserClinicAssignmentPostgresIntegrationTests
{
    private const string RequiredDatabase = "mhs_export_ef_review";
    private static readonly Guid PgCenter = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid PgClinic = Guid.Parse("c2222222-2222-2222-2222-222222222222");

    /// <summary>يُستخدم في التقرير لمعرفة إن شُغّل اختبار PG فعلياً.</summary>
    public static bool LastRunExecuted { get; private set; }

    [Fact]
    public async Task Postgres_effective_doctors_count_query_translates_and_matches_active_count_when_fully_eligible()
    {
        var cs = ResolveConnectionString();
        if (cs is null)
            return;

        await EnsureReviewRoleSchemaAsync(cs);
        await using var db = CreatePostgresContext(cs);
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var actorId = $"pg-clinic-{Guid.NewGuid():N}"[..20];
            var doctorEligible = $"pg-doc-ok-{Guid.NewGuid():N}"[..20];
            var doctorNoRole = $"pg-doc-nr-{Guid.NewGuid():N}"[..20];

            await EnsureContextActorUserAsync(db, actorId);
            await SeedCenterClinicAsync(db, PgCenter, PgClinic);
            await SeedDoctorAsync(db, doctorEligible, PgCenter);
            await SeedUserWithAssignmentOnlyAsync(db, doctorNoRole, PgCenter, PgClinic, actorId);
            await SeedSuperAdminAsync(db, actorId);

            var probeQuery = EffectiveDoctorsUserIdsProbe(db, PgCenter, PgClinic);
            var probeSql = probeQuery.ToQueryString();
            Assert.Contains("SELECT", probeSql, StringComparison.OrdinalIgnoreCase);
            var probeCount = await probeQuery.CountAsync();
            Assert.Equal(1, probeCount);

            var service = CreateService(db, actorId);
            var page = await service.GetCenterClinicsAsync(PgCenter, 1, 10);

            var item = Assert.Single(page.Items, c => c.ClinicId == PgClinic);
            Assert.Equal(2, item.ActiveDoctorsCount);
            Assert.Equal(1, item.EffectiveDoctorsCount);
        }
        finally
        {
            await tx.RollbackAsync();
            LastRunExecuted = true;
        }
    }

    private static IQueryable<string> EffectiveDoctorsUserIdsProbe(AppDbContext db, Guid centerId, Guid clinicId)
    {
        var userCenters = db.UserCenters.AsQueryable();
        var roleAssociations = db.UserRoleAssociations.AsQueryable();
        return db.UserClinicAssignments
            .Where(a => a.CenterId == centerId
                && a.ClinicId == clinicId
                && a.IsActive
                && a.User.IsActive)
            .Where(a => userCenters.Any(uc =>
                uc.UserId == a.UserId && uc.CenterId == centerId && uc.IsActive))
            .Where(a => roleAssociations.Any(ura =>
                ura.UserId == a.UserId
                && ura.CenterId == centerId
                && ura.IsActive
                && ura.Role.IsActive
                && ura.Role.Name == UserClinicAssignmentService.ClinicDoctorRoleName))
            .Select(a => a.UserId)
            .Distinct();
    }

    private static UserClinicAssignmentService CreateService(AppDbContext db, string actorId)
    {
        var users = new UserRepository(db);
        var centers = new MedicalCenterRepository(db);
        var centerClinics = new CenterClinicRepository(db);
        var access = new AccessContextRepository(db);
        var uow = new TestUnitOfWork(db);

        var manager = new Mock<UserManager<User>>(
            Mock.Of<IUserStore<User>>(),
            Mock.Of<IOptions<IdentityOptions>>(),
            Mock.Of<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            Mock.Of<ILookupNormalizer>(),
            Mock.Of<IdentityErrorDescriber>(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<User>>>());
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns((string id) => db.Users.FirstOrDefaultAsync(u => u.Id == id));

        var permissions = new[]
        {
            SystemPermissions.UserManagement.Read,
            MilitaryHealthPermissions.UserCenters.Manage,
            MilitaryHealthPermissions.UserClinics.Manage
        };

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.GetPermissions()).Returns(permissions);
        gate.Setup(g => g.EnsureAll(It.IsAny<string[]>()))
            .Callback((string[] required) =>
            {
                if (required.Any(r => !permissions.Contains(r, StringComparer.Ordinal)))
                    throw new ForbiddenPermissionException();
            });
        gate.Setup(g => g.EnsureLocalAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid? clientCenterId, CancellationToken __) =>
                clientCenterId ?? PgCenter);

        var current = new TestCurrentUser();
        current.SetUser(actorId, actorId, "127.0.0.1");

        return new UserClinicAssignmentService(
            manager.Object,
            users,
            centers,
            centerClinics,
            access,
            uow,
            gate.Object,
            current);
    }

    private static async Task SeedCenterClinicAsync(AppDbContext db, Guid centerId, Guid clinicId)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await db.MedicalCenters.AnyAsync(c => c.CenterId == centerId))
        {
            db.MedicalCenters.Add(new MedicalCenter
            {
                CenterId = centerId,
                Code = "PG-CL",
                NameAr = "مركز PG عيادات",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.Clinics.AnyAsync(c => c.ClinicId == clinicId))
        {
            db.Clinics.Add(new Clinic
            {
                ClinicId = clinicId,
                Code = "PGC",
                NameAr = "عيادة PG",
                SortOrder = 1,
                CountsForFinal = true,
                IsActive = true
            });
        }

        if (!await db.CenterClinics.AnyAsync(x => x.CenterId == centerId && x.ClinicId == clinicId))
        {
            db.CenterClinics.Add(new CenterClinic
            {
                CenterClinicId = Guid.NewGuid(),
                CenterId = centerId,
                ClinicId = clinicId,
                IsActive = true,
                ActivatedAt = now
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedDoctorAsync(AppDbContext db, string userId, Guid centerId)
    {
        await EnsureUserAsync(db, userId, userId);
        await EnsureUserCenterAsync(db, userId, centerId);
        var roleId = await EnsureClinicDoctorRoleAsync(db);
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
        db.UserClinicAssignments.Add(new UserClinicAssignment
        {
            AssignmentId = Guid.NewGuid(),
            UserId = userId,
            CenterId = centerId,
            ClinicId = PgClinic,
            SpecialtyTitle = "اختبار",
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = userId
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedUserWithAssignmentOnlyAsync(
        AppDbContext db,
        string userId,
        Guid centerId,
        Guid clinicId,
        string assignedBy)
    {
        await EnsureUserAsync(db, userId, userId);
        await EnsureUserCenterAsync(db, userId, centerId);
        db.UserClinicAssignments.Add(new UserClinicAssignment
        {
            AssignmentId = Guid.NewGuid(),
            UserId = userId,
            CenterId = centerId,
            ClinicId = clinicId,
            SpecialtyTitle = "بدون دور",
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = assignedBy
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> EnsureClinicDoctorRoleAsync(AppDbContext db)
    {
        var existing = await db.Roles.FirstOrDefaultAsync(r => r.Name == UserClinicAssignmentService.ClinicDoctorRoleName);
        if (existing is not null)
            return existing.Id;

        var role = new Role
        {
            Id = Guid.NewGuid().ToString(),
            Name = UserClinicAssignmentService.ClinicDoctorRoleName,
            NormalizedName = UserClinicAssignmentService.ClinicDoctorRoleName.ToUpperInvariant(),
            Scope = AssignableRolePolicy.CatalogScopeForName(UserClinicAssignmentService.ClinicDoctorRoleName),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private static async Task SeedSuperAdminAsync(AppDbContext db, string actorId)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "SuperAdmin");
        if (role is null)
        {
            role = new Role
            {
                Id = Guid.NewGuid().ToString(),
                Name = "SuperAdmin",
                NormalizedName = "SUPERADMIN",
                Scope = AssignableRolePolicy.CatalogScopeForName("SuperAdmin"),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        if (!await db.UserRoleAssociations.AnyAsync(x => x.UserId == actorId && x.RoleId == role.Id && x.IsActive))
        {
            db.UserRoleAssociations.Add(new UserRoleAssociation
            {
                Id = Guid.NewGuid(),
                UserId = actorId,
                RoleId = role.Id,
                CenterId = null,
                IsActive = true,
                AssignedByUserId = actorId,
                AssignedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureUserAsync(AppDbContext db, string id, string userName)
    {
        if (await db.Users.AnyAsync(u => u.Id == id))
            return;

        db.Users.Add(new User
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@pg.local",
            NormalizedEmail = $"{userName.ToUpperInvariant()}@PG.LOCAL",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            CreationDate = DateTime.UtcNow
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
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = userId
        });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureContextActorUserAsync(AppDbContext db, string actorId)
    {
        await EnsureUserAsync(db, "pg-actor", "pg.context.actor");
        await EnsureUserAsync(db, actorId, actorId);
    }

    private static string? ResolveConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("MHS_EXPORT_EF_REVIEW_PG");
        if (!string.IsNullOrWhiteSpace(cs))
        {
            var builder = new NpgsqlConnectionStringBuilder(cs.Trim());
            if (!string.Equals(builder.Database, RequiredDatabase, StringComparison.OrdinalIgnoreCase))
                return null;
            return builder.ConnectionString;
        }

        var pwdPath = Path.Combine(Path.GetTempPath(), "mhs_review_tester_pwd.txt");
        if (!File.Exists(pwdPath))
            return null;

        var pwd = File.ReadAllText(pwdPath).Trim();
        return $"Host=127.0.0.1;Port=5432;Database={RequiredDatabase};Username=mhs_review_tester;Password={pwd}";
    }

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
        return new AppDbContext(options, user, new TestCenter(null, central: true), new AuditInterceptor(user));
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
