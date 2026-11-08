using Application.Common.Permissions;
using Application.Dtos.Centers;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Mappings;
using Application.Services.Centers;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class MedicalCenterServiceTests
{
    [Fact]
    public async Task Create_rejects_duplicate_code()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        await service.CreateAsync(new CreateMedicalCenterDto
        {
            Code = "CTR-01",
            NameAr = "مركز أول",
            IsActive = true
        });

        var error = await Assert.ThrowsAsync<MedicalCenterCodeAlreadyExistsException>(() =>
            service.CreateAsync(new CreateMedicalCenterDto
            {
                Code = "ctr-01",
                NameAr = "مركز مكرر",
                IsActive = true
            }));

        Assert.Equal(400, error.Status);
        Assert.Equal(1, await db.MedicalCenters.CountAsync());
    }

    [Fact]
    public async Task Update_does_not_change_code_or_id()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        var created = await service.CreateAsync(new CreateMedicalCenterDto
        {
            Code = "CTR-02",
            NameAr = "اسم قديم",
            LocationText = "طرابلس",
            IsActive = true
        });

        var updated = await service.UpdateAsync(created.CenterId, new UpdateMedicalCenterDto
        {
            NameAr = "اسم جديد",
            LocationText = "بنغازي",
            ParentOrgText = "قيادة طبية",
            ContactInfo = "123"
        });

        Assert.Equal(created.CenterId, updated.CenterId);
        Assert.Equal("CTR-02", updated.Code);
        Assert.Equal("اسم جديد", updated.NameAr);
        Assert.Equal("بنغازي", updated.LocationText);

        var row = await db.MedicalCenters.SingleAsync(c => c.CenterId == created.CenterId);
        Assert.Equal("CTR-02", row.Code);
    }

    [Fact]
    public async Task Manage_without_permission_is_forbidden()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: false);

        var error = Assert.Throws<ForbiddenPermissionException>(() =>
            service.CreateAsync(new CreateMedicalCenterDto
            {
                Code = "CTR-03",
                NameAr = "مركز",
                IsActive = true
            }).GetAwaiter().GetResult());

        Assert.Equal(403, error.Status);
        Assert.Equal(0, await db.MedicalCenters.CountAsync());
    }

    [Fact]
    public async Task EnsureAcceptsNewRelations_rejects_inactive_center()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        var created = await service.CreateAsync(new CreateMedicalCenterDto
        {
            Code = "CTR-04",
            NameAr = "مركز للتعطيل",
            IsActive = true
        });

        await service.DeactivateAsync(created.CenterId, new DeactivateMedicalCenterDto
        {
            Reason = "صيانة مؤقتة"
        });

        var error = await Assert.ThrowsAsync<InactiveMedicalCenterException>(() =>
            service.EnsureAcceptsNewRelationsAsync(created.CenterId));

        Assert.Equal(400, error.Status);

        var row = await db.MedicalCenters.SingleAsync(c => c.CenterId == created.CenterId);
        Assert.False(row.IsActive);
        Assert.Equal("صيانة مؤقتة", row.DeactivationReason);
    }

    [Fact]
    public async Task Access_context_excludes_inactive_center_from_assignment_options()
    {
        await using var db = CreateDb();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.MedicalCenters.AddRange(
            new MedicalCenter
            {
                CenterId = activeId,
                Code = "A1",
                NameAr = "فعّال",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new MedicalCenter
            {
                CenterId = inactiveId,
                Code = "B1",
                NameAr = "معطّل",
                IsActive = false,
                DeactivationReason = "إيقاف",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.Users.Add(new User
        {
            Id = "user-1",
            UserName = "u1",
            NormalizedUserName = "U1",
            Email = "u1@test.local",
            NormalizedEmail = "U1@TEST.LOCAL",
            SecurityStamp = Guid.NewGuid().ToString(),
            IsActive = true
        });
        db.UserCenters.AddRange(
            new UserCenter
            {
                UserId = "user-1",
                CenterId = activeId,
                IsActive = true,
                AssignedByUserId = "user-1",
                AssignedAt = now
            },
            new UserCenter
            {
                UserId = "user-1",
                CenterId = inactiveId,
                IsActive = true,
                AssignedByUserId = "user-1",
                AssignedAt = now
            });
        await db.SaveChangesAsync();

        var access = new AccessContextRepository(db);
        var centers = await access.GetActiveCentersAsync("user-1");
        Assert.Single(centers);
        Assert.Equal(activeId, centers[0].CenterId);
        Assert.False(await access.IsActivelyAssignedAsync("user-1", inactiveId));
        Assert.True(await access.IsActivelyAssignedAsync("user-1", activeId));
    }

    [Fact]
    public async Task Deactivate_requires_reason_and_keeps_row()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        var created = await service.CreateAsync(new CreateMedicalCenterDto
        {
            Code = "CTR-05",
            NameAr = "للتعطيل",
            IsActive = true
        });

        await Assert.ThrowsAsync<InvalidMedicalCenterDataException>(() =>
            service.DeactivateAsync(created.CenterId, new DeactivateMedicalCenterDto { Reason = "  " }));

        var deactivated = await service.DeactivateAsync(created.CenterId, new DeactivateMedicalCenterDto
        {
            Reason = "نقل العمليات"
        });

        Assert.False(deactivated.IsActive);
        Assert.Equal(1, await db.MedicalCenters.CountAsync());
    }

    [Fact]
    public async Task Create_and_deactivate_are_audited_with_old_and_new_values()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        var created = await service.CreateAsync(new CreateMedicalCenterDto
        {
            Code = "AUD-1",
            NameAr = "مركز تدقيق",
            IsActive = true
        });

        var createAudit = await db.AuditEvents.SingleAsync(e =>
            e.EntityType == nameof(MedicalCenter) &&
            e.EntityId == created.CenterId.ToString() &&
            e.EventType == "Added");
        Assert.Null(createAudit.OldValues);
        Assert.Contains("AUD-1", createAudit.NewValues);
        Assert.Equal("actor-1", createAudit.ActorUserId);

        await service.DeactivateAsync(created.CenterId, new DeactivateMedicalCenterDto
        {
            Reason = "سبب التدقيق"
        });

        var deactivateAudit = await db.AuditEvents
            .Where(e =>
                e.EntityType == nameof(MedicalCenter) &&
                e.EntityId == created.CenterId.ToString() &&
                e.EventType == "Modified")
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();
        Assert.Contains("\"IsActive\":true", deactivateAudit.OldValues);
        Assert.Contains("\"IsActive\":false", deactivateAudit.NewValues);
        Assert.Contains("سبب التدقيق", deactivateAudit.NewValues);
    }

    [Fact]
    public async Task List_supports_search_filter_and_pagination()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        await service.CreateAsync(new CreateMedicalCenterDto { Code = "EAST-1", NameAr = "الشرق", IsActive = true });
        await MarkVerifiedAsync(db);
        await service.CreateAsync(new CreateMedicalCenterDto { Code = "WEST-1", NameAr = "الغرب", IsActive = true });
        await service.CreateAsync(new CreateMedicalCenterDto
        {
            Code = "OLD-1",
            NameAr = "قديم",
            IsActive = false,
            DeactivationReason = "إغلاق"
        });

        var page = await service.GetCentersAsync(1, 10, search: "شرق", isActive: true);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("EAST-1", Assert.Single(page.Items).Code);
    }

    private static async Task MarkVerifiedAsync(AppDbContext db)
    {
        var centerId = await db.MedicalCenters.Select(c => c.CenterId).FirstAsync();
        db.HistoricalCenterMigrationCompletions.Add(new HistoricalCenterMigrationCompletion
        {
            Id = Guid.NewGuid(),
            TargetCenterId = centerId,
            VerifiedAt = DateTimeOffset.UtcNow,
            VerifiedByUserId = "actor-1",
            SummaryJson = "[]",
            ActiveCenterCountAtVerification = 1,
            PreExistingMultiCenterDetected = false
        });
        await db.SaveChangesAsync();
    }

    private static MedicalCenterService CreateService(AppDbContext db, bool allowManage)
    {
        var gate = new Mock<IPermissionGate>();
        if (!allowManage)
        {
            gate.Setup(g => g.EnsureAll(It.IsAny<string[]>()))
                .Throws(new ForbiddenPermissionException());
        }
        else
        {
            gate.Setup(g => g.EnsureAll(It.Is<string[]>(p =>
                p.Contains(MilitaryHealthPermissions.Centers.Manage))));
        }

        var config = new TypeAdapterConfig();
        new MedicalCenterProfile().Register(config);
        var mapper = new Mapper(config);

        return new MedicalCenterService(
            new MedicalCenterRepository(db),
            new TestUnitOfWork(db),
            mapper,
            gate.Object,
            CreateMigration(db, gate.Object));
    }

    private static HistoricalCenterMigrationService CreateMigration(AppDbContext db, IPermissionGate gate)
        => new(
            new HistoricalCenterMigrationRepository(db),
            new TestUnitOfWork(db),
            new AuditLogRepository(db),
            new TestCurrentUser(),
            gate);

    private static AppDbContext CreateDb()
    {
        var user = new TestCurrentUser();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
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
            => throw new NotSupportedException();
    }
}
