using System.Reflection;
using System.Text;
using System.Text.Json;
using API.Controllers;
using API.Middleware;
using Application.Common.Permissions;
using Application.Dtos.ExportDtos;
using Application.Dtos.ReportDtos;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Mappings;
using Application.ModelsDto.AuditLogDtos;
using Application.Services;
using Application.Services.Audit;
using Application.Services.Reports;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Application.Tests;

/// <summary>
/// MH-P7-090 / FR-076…079: فلاتر المدقق، التصدير مع تدقيق ذاتي، الاطلاع العابر، أخطاء آمنة، عدم قابلية تعديل السجل.
/// </summary>
public class AuditSecurityHardeningTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Audit_list_filters_by_actor_event_center_entity_and_period()
    {
        await using var db = CreateDb();
        await SeedCentersAndUserAsync(db);

        var now = DateTimeOffset.UtcNow;
        db.AuditEvents.AddRange(
            Ev("actor-1", "MemberCreate", CenterA, "Member", now.AddHours(-2)),
            Ev("actor-1", "MemberTransferExecuted", CenterB, "Member", now.AddHours(-1)),
            Ev("actor-2", "VIEW", CenterA, "MemberArchive", now.AddMinutes(-30)),
            Ev("actor-1", "MemberCreate", CenterA, "Member", now.AddDays(-10)));
        await db.SaveChangesAsync();

        var service = CreateAuditService(db, allowRead: true);
        var page = await service.GetAllFilteredAsync(1, 50, new AuditLogFilter
        {
            actorUserId = "actor-1",
            eventType = "MemberCreate",
            centerId = CenterA,
            entityType = "Member",
            From = now.AddDays(-1),
            To = now.AddHours(1)
        });

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("MemberCreate", Assert.Single(page.Items).ActionType);
    }

    [Fact]
    public async Task Audit_read_requires_permission()
    {
        await using var db = CreateDb();
        var service = CreateAuditService(db, allowRead: false);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetAllFilteredAsync(1, 10, new AuditLogFilter()));
    }

    [Fact]
    public async Task Audit_export_requires_export_permission_and_writes_self_audit()
    {
        await using var db = CreateDb();
        await SeedCentersAndUserAsync(db);
        db.AuditEvents.Add(Ev("actor-1", "MemberCreate", CenterA, "Member", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var export = new Mock<IExportService>();
        export.Setup(e => e.ExportToExcelAsync(
                It.IsAny<IEnumerable<AuditLogReportRowDto>>(),
                It.IsAny<ExportOptions>()))
            .ReturnsAsync(new ExportFileDto
            {
                FileContents = new byte[] { 1, 2, 3 },
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileName = "audit.xlsx"
            });

        var denied = CreateReportsService(db, export.Object, allowExport: false);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            denied.ExportAuditLogsAsync(new AuditLogFilter(), "excel"));

        var allowed = CreateReportsService(db, export.Object, allowExport: true);
        var file = await allowed.ExportAuditLogsAsync(new AuditLogFilter { centerId = CenterA }, "excel");
        Assert.Equal("audit.xlsx", file.FileName);

        var self = Assert.Single(await db.AuditEvents
            .Where(e => e.EventType == ReportsService.AuditExportEventType)
            .ToListAsync());
        Assert.Equal("SUCCESS", self.Result);
        Assert.Equal(nameof(AuditEvent), self.EntityType);
        Assert.Contains("RecordCount", self.NewValues);
        Assert.DoesNotContain("MemberCreate", self.NewValues ?? "");
    }

    [Fact]
    public async Task Cross_center_same_local_center_is_noop_without_reason_rejection()
    {
        await using var db = CreateDb();
        await SeedCentersAndUserAsync(db);

        var service = new CrossCenterAccessService(
            new CrossCenterAccessRepository(db),
            new TestCurrentUser(),
            new TestCenter(CenterA, central: false),
            new TestUnitOfWork(db));

        await service.RecordAsync(CenterA, "سبب", "Member", null, "Member", "1");
        Assert.Empty(await db.CrossCenterAccesses.ToListAsync());
        Assert.Empty(await db.AuditEvents.Where(e => e.EventType == "VIEW").ToListAsync());
    }

    [Fact]
    public async Task Cross_center_records_access_with_reason_from_central_context()
    {
        await using var db = CreateDb();
        await SeedCentersAndUserAsync(db);

        var service = new CrossCenterAccessService(
            new CrossCenterAccessRepository(db),
            new TestCurrentUser(),
            new TestCenter(null, central: true),
            new TestUnitOfWork(db));

        await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.RecordAsync(CenterB, "  ", "Member", null, "Member", "1"));

        await service.RecordAsync(CenterB, "مراجعة مركزية", "Member", "file=9", "Member", "42");

        var access = Assert.Single(await db.CrossCenterAccesses.Include(a => a.AuditEvent).ToListAsync());
        Assert.Equal(CenterB, access.TargetCenterId);
        Assert.Equal("مراجعة مركزية", access.AccessReason);
        Assert.Equal("Member", access.DataType);
        Assert.NotNull(access.AuditEvent);
        Assert.Equal("VIEW", access.AuditEvent!.EventType);
        Assert.Equal("مراجعة مركزية", access.AuditEvent.ReasonText);
    }

    [Fact]
    public void AuditLog_controller_has_no_mutate_http_verbs()
    {
        var methods = typeof(AuditLogController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            Assert.Null(method.GetCustomAttribute<HttpPutAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpDeleteAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpPatchAttribute>());
        }

        Assert.DoesNotContain(
            MilitaryHealthPermissions.AuditLog.Modify,
            MilitaryHealthSeedCatalog.MatrixGrants.SelectMany(g => g.PermissionNames));
    }

    [Fact]
    public async Task Unexpected_error_response_includes_correlation_without_stack_or_internals()
    {
        var correlation = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.CorrelationId).Returns(correlation);

        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(current.Object);
        http.RequestServices = services.BuildServiceProvider();

        var middleware = new ErrorHandlingMiddleware(
            _ => throw new Exception("SQL SELECT * FROM secret_table -- leak"),
            NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(http);

        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEndAsync();
        Assert.Equal(500, http.Response.StatusCode);
        Assert.Equal(correlation.ToString("D"), http.Response.Headers["X-Correlation-Id"].ToString());
        Assert.Contains("UNEXPECTED_SERVER_ERROR", body);
        Assert.Contains(correlation.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL SELECT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret_table", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("originalError", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_appsettings_does_not_embed_connection_or_jwt_secrets()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "API", "appsettings.Production.json"));
        Assert.True(File.Exists(path), path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("ConnectionStrings", out _));
        Assert.False(root.TryGetProperty("JwtSettings", out _));
    }

    private static AuditEvent Ev(
        string actor,
        string eventType,
        Guid? centerId,
        string entityType,
        DateTimeOffset at)
        => new()
        {
            OccurredAt = at,
            ActorUserId = actor,
            ContextScope = centerId.HasValue ? "CENTER" : "CENTRAL",
            CenterId = centerId,
            EventType = eventType,
            Result = "SUCCESS",
            EntityType = entityType,
            EntityId = Guid.NewGuid().ToString(),
            CorrelationId = Guid.NewGuid()
        };

    private static AuditLogService CreateAuditService(AppDbContext db, bool allowRead)
    {
        var config = new TypeAdapterConfig();
        new AuditLogProfile().Register(config);
        var gate = new Mock<IPermissionGate>();
        if (allowRead)
            gate.Setup(g => g.EnsureAll(It.IsAny<string[]>()));
        else
            gate.Setup(g => g.EnsureAll(It.IsAny<string[]>())).Throws(new ForbiddenPermissionException());

        return new AuditLogService(
            new AuditLogRepository(db),
            new UserRepository(db),
            new Mapper(config),
            gate.Object);
    }

    private static ReportsService CreateReportsService(AppDbContext db, IExportService export, bool allowExport)
    {
        var config = new TypeAdapterConfig();
        new AuditLogProfile().Register(config);
        var gate = new Mock<IPermissionGate>();
        if (allowExport)
            gate.Setup(g => g.EnsureAll(It.IsAny<string[]>()));
        else
            gate.Setup(g => g.EnsureAll(It.IsAny<string[]>())).Throws(new ForbiddenPermissionException());

        return new ReportsService(
            new AuditLogRepository(db),
            new UserRepository(db),
            export,
            Mock.Of<IDashboardService>(),
            new Mapper(config),
            gate.Object,
            new TestCurrentUser(),
            new TestUnitOfWork(db));
    }

    private static async Task SeedCentersAndUserAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.Users.AddRange(
            new User
            {
                Id = "actor-1",
                UserName = "actor1",
                NormalizedUserName = "ACTOR1",
                Email = "a1@test.local",
                NormalizedEmail = "A1@TEST.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString(),
                IsActive = true,
                FullName = "Actor One"
            },
            new User
            {
                Id = "actor-2",
                UserName = "actor2",
                NormalizedUserName = "ACTOR2",
                Email = "a2@test.local",
                NormalizedEmail = "A2@TEST.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString(),
                IsActive = true,
                FullName = "Actor Two"
            });
        db.MedicalCenters.AddRange(
            new MedicalCenter
            {
                CenterId = CenterA,
                Code = "A",
                NameAr = "أ",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new MedicalCenter
            {
                CenterId = CenterB,
                Code = "B",
                NameAr = "ب",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
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
