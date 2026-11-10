using Application.Common.Permissions;
using Application.Dtos.DashboardDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.Dashboard;
using Domain.Entities.Audit;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using MapsterMapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;

namespace Application.Tests;

public class RbacHardeningTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Dashboard_without_audit_permission_returns_no_activity()
    {
        await using var db = CreateDb();
        await SeedEventAsync(db, CenterA, "user-a");
        await SeedEventAsync(db, CenterB, "user-b");

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.Has(SystemPermissions.AuditLog.Read)).Returns(false);
        var stats = await CreateDashboard(db, gate.Object, new TestCenter(CenterA, false)).GetAsync(new DashboardFilter());

        Assert.Empty(stats.RecentActivities);
        Assert.Empty(stats.MostActiveUsers);
    }

    [Fact]
    public async Task Dashboard_local_reader_sees_only_the_active_center()
    {
        await using var db = CreateDb();
        await SeedEventAsync(db, CenterA, "user-a");
        await SeedEventAsync(db, CenterB, "user-b");
        await SeedEventAsync(db, null, "central-user");

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.Has(SystemPermissions.AuditLog.Read)).Returns(true);
        gate.Setup(g => g.EnsureLocalAsync(SystemPermissions.Dashboard.Read, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CenterA);

        var service = CreateDashboard(db, gate.Object, new TestCenter(CenterA, false));
        var stats = await service.GetAsync(new DashboardFilter { RecentCount = 10, TopUsersCount = 10 });

        Assert.Single(stats.RecentActivities);
        Assert.Equal(CenterA, stats.RecentActivities[0].CenterId);
        Assert.Single(stats.MostActiveUsers);
        Assert.Equal("user-a", stats.MostActiveUsers[0].UserId);

        var foreign = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetAsync(new DashboardFilter { CenterId = CenterB }));
        Assert.Contains("غير المركز النشط", foreign.Message);
    }

    [Fact]
    public async Task Dashboard_central_reader_can_narrow_to_an_authorized_center()
    {
        await using var db = CreateDb();
        await SeedEventAsync(db, CenterA, "user-a");
        await SeedEventAsync(db, CenterB, "user-b");

        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.Has(SystemPermissions.AuditLog.Read)).Returns(true);
        gate.Setup(g => g.EnsureCentralAsync(SystemPermissions.AuditLog.Read, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateDashboard(db, gate.Object, new TestCenter(null, true));
        var all = await service.GetAsync(new DashboardFilter { RecentCount = 10, TopUsersCount = 10 });
        var onlyB = await service.GetAsync(new DashboardFilter
        {
            CenterId = CenterB,
            RecentCount = 10,
            TopUsersCount = 10
        });

        Assert.Equal(2, all.RecentActivities.Count);
        Assert.Equal(2, all.MostActiveUsers.Count);
        Assert.Single(onlyB.RecentActivities);
        Assert.Equal(CenterB, onlyB.RecentActivities[0].CenterId);
    }

    [Fact]
    public async Task Role_permission_version_conflict_uses_two_sqlite_connections()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mhs-rbac-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<RoleVersionProbeContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            await using (var setup = new RoleVersionProbeContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Roles.Add(new RoleVersionRow { Id = "role-1", Name = "CustomOps", PermissionSetVersion = 2 });
                await setup.SaveChangesAsync();
            }

            await using var first = new RoleVersionProbeContext(options);
            await using var second = new RoleVersionProbeContext(options);
            var left = await first.Roles.SingleAsync(r => r.Id == "role-1");
            var right = await second.Roles.SingleAsync(r => r.Id == "role-1");

            left.PermissionSetVersion = 3;
            await first.SaveChangesAsync();

            right.PermissionSetVersion = 3;
            var conflict = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            Assert.NotEmpty(conflict.Entries);

            await using var check = new RoleVersionProbeContext(options);
            var saved = await check.Roles.AsNoTracking().SingleAsync(r => r.Id == "role-1");
            Assert.Equal(3, saved.PermissionSetVersion);
            Assert.Equal("CustomOps", saved.Name);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// يحاكي رمز التزامن نفسه الموجود على Role.PermissionSetVersion.
    /// نموذج التطبيق الكامل يستخدم دوال PostgreSQL ولا يُنشأ على SQLite.
    /// </summary>
    private sealed class RoleVersionRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long PermissionSetVersion { get; set; }
    }

    private sealed class RoleVersionProbeContext : DbContext
    {
        public RoleVersionProbeContext(DbContextOptions<RoleVersionProbeContext> options) : base(options) { }
        public DbSet<RoleVersionRow> Roles => Set<RoleVersionRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RoleVersionRow>(builder =>
            {
                builder.HasKey(r => r.Id);
                builder.Property(r => r.PermissionSetVersion).IsConcurrencyToken();
            });
        }
    }

    [Fact]
    public async Task Role_permission_version_conflict_uses_two_postgres_connections()
    {
        var connectionString = ResolveReviewConnection();
        if (connectionString is null)
            return;

        const string probeRoleId = "rbac-concurrency-probe";
        var columnAdded = false;
        var probeInserted = false;
        var migrationApplied = false;
        try
        {
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                migrationApplied = await VersionMigrationAppliedAsync(connection);
                columnAdded = await EnsureVersionColumnAsync(connection);
                await using var insert = new NpgsqlCommand(
                    """
                    INSERT INTO iam."Roles"
                        ("Id", "Name", "NormalizedName", "ConcurrencyStamp", "IsActive", "Scope", "PermissionSetVersion", "CreatedAt")
                    VALUES
                        (@id, 'RbacProbe', 'RBACPROBE', 'probe', TRUE, 0, 2, NOW())
                    ON CONFLICT ("Id") DO UPDATE
                        SET "Name" = 'RbacProbe', "IsActive" = TRUE, "PermissionSetVersion" = 2
                    """,
                    connection);
                insert.Parameters.AddWithValue("id", probeRoleId);
                await insert.ExecuteNonQueryAsync();
                probeInserted = true;
            }

            var options = new DbContextOptionsBuilder<PostgresRoleVersionContext>()
                .UseNpgsql(connectionString)
                .Options;
            await using var first = new PostgresRoleVersionContext(options);
            await using var second = new PostgresRoleVersionContext(options);
            var left = await first.Roles.SingleAsync(r => r.Id == probeRoleId);
            var right = await second.Roles.SingleAsync(r => r.Id == probeRoleId);
            Assert.Equal(2, left.PermissionSetVersion);
            Assert.Equal(2, right.PermissionSetVersion);

            left.PermissionSetVersion = 3;
            await first.SaveChangesAsync();

            right.PermissionSetVersion = 3;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

            await using var check = new PostgresRoleVersionContext(options);
            Assert.Equal(3, (await check.Roles.AsNoTracking().SingleAsync(r => r.Id == probeRoleId)).PermissionSetVersion);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            if (probeInserted)
            {
                await using var retire = new NpgsqlCommand(
                    """UPDATE iam."Roles" SET "IsActive" = FALSE, "PermissionSetVersion" = 2 WHERE "Id" = @id""",
                    connection);
                retire.Parameters.AddWithValue("id", probeRoleId);
                await retire.ExecuteNonQueryAsync();
            }

            if (columnAdded || !migrationApplied)
            {
                await using var drop = new NpgsqlCommand(
                    "ALTER TABLE iam.\"Roles\" DROP COLUMN IF EXISTS \"PermissionSetVersion\"",
                    connection);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task<bool> VersionMigrationAppliedAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """SELECT COUNT(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%AddRolePermissionSetVersion'""",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> EnsureVersionColumnAsync(NpgsqlConnection connection)
    {
        await using var check = new NpgsqlCommand(
            """SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'iam' AND table_name = 'Roles' AND column_name = 'PermissionSetVersion'""",
            connection);
        var existing = Convert.ToInt32(await check.ExecuteScalarAsync());
        if (existing > 0)
            return false;

        await using var add = new NpgsqlCommand(
            """ALTER TABLE iam."Roles" ADD COLUMN "PermissionSetVersion" bigint NOT NULL DEFAULT 0""",
            connection);
        await add.ExecuteNonQueryAsync();
        return true;
    }

    private static string? ResolveReviewConnection()
    {
        var configured = Environment.GetEnvironmentVariable("MHS_EXPORT_EF_REVIEW_PG");
        string? connectionString = null;
        if (!string.IsNullOrWhiteSpace(configured))
            connectionString = configured.Trim();
        else
        {
            var pwdPath = Path.Combine(Path.GetTempPath(), "mhs_review_tester_pwd.txt");
            if (!File.Exists(pwdPath))
                return null;
            var password = File.ReadAllText(pwdPath).Trim();
            connectionString = $"Host=127.0.0.1;Port=5432;Database=mhs_export_ef_review;Username=mhs_review_tester;Password={password}";
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.Database, "mhs_export_ef_review", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing a database other than the isolated review database.");
        return connectionString;
    }

    private sealed class PostgresRoleVersionContext : DbContext
    {
        public PostgresRoleVersionContext(DbContextOptions<PostgresRoleVersionContext> options) : base(options) { }
        public DbSet<RoleVersionRow> Roles => Set<RoleVersionRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RoleVersionRow>(builder =>
            {
                builder.ToTable("Roles", "iam");
                builder.HasKey(r => r.Id);
                builder.Property(r => r.Id).HasColumnName("Id");
                builder.Property(r => r.PermissionSetVersion).HasColumnName("PermissionSetVersion").IsConcurrencyToken();
                builder.Ignore(r => r.Name);
            });
        }
    }

    private static DashboardService CreateDashboard(AppDbContext db, IPermissionGate gate, ICurrentCenterService center)
    {
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<List<RecentActivityDto>>(It.IsAny<object>()))
            .Returns((object source) => ((IEnumerable<AuditEvent>)source)
                .Select(e => new RecentActivityDto
                {
                    Id = e.EventId,
                    UserId = e.ActorUserId ?? string.Empty,
                    CenterId = e.CenterId
                })
                .ToList());

        return new DashboardService(
            new AuditLogRepository(db),
            new UserRepository(db),
            Mock.Of<IUserContextService>(),
            mapper.Object,
            gate,
            center);
    }

    private static async Task SeedEventAsync(AppDbContext db, Guid? centerId, string actorId)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = actorId,
            CenterId = centerId,
            EventType = "UserUpdated",
            Result = "SUCCESS",
            CorrelationId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateDb()
    {
        var user = new TestCurrentUser();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, user, new TestCenter(null, true), new AuditInterceptor(user));
    }

}
