using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.Centers;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.Centers;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class CenterClinicServiceTests
{
    [Fact]
    public async Task Link_same_clinic_to_two_centers_succeeds()
    {
        await using var db = CreateDb();
        var (service, centerA, centerB, clinic) = await SeedTwoCentersOneClinicAsync(db);

        var linkA = await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic });
        var linkB = await service.LinkAsync(centerB, new LinkCenterClinicDto { ClinicId = clinic });

        Assert.True(linkA.IsActive);
        Assert.True(linkB.IsActive);
        Assert.Equal(2, await db.CenterClinics.CountAsync());
        Assert.Equal(clinic, linkA.ClinicId);
        Assert.Equal(clinic, linkB.ClinicId);
        Assert.NotEqual(linkA.CenterId, linkB.CenterId);
    }

    [Fact]
    public async Task Link_duplicate_in_same_center_is_rejected()
    {
        await using var db = CreateDb();
        var (service, centerA, _, clinic) = await SeedTwoCentersOneClinicAsync(db);

        await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic });

        var error = await Assert.ThrowsAsync<CenterClinicAlreadyExistsException>(() =>
            service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic }));

        Assert.Equal(400, error.Status);
        Assert.Equal(1, await db.CenterClinics.CountAsync());
    }

    [Fact]
    public async Task Deactivate_in_one_center_keeps_other_center_active()
    {
        await using var db = CreateDb();
        var (service, centerA, centerB, clinic) = await SeedTwoCentersOneClinicAsync(db);

        var linkA = await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic });
        var linkB = await service.LinkAsync(centerB, new LinkCenterClinicDto { ClinicId = clinic });

        var deactivated = await service.DeactivateAsync(centerA, linkA.CenterClinicId);

        Assert.False(deactivated.IsActive);
        Assert.NotNull(deactivated.DeactivatedAt);

        var stillActive = await db.CenterClinics.SingleAsync(x => x.CenterClinicId == linkB.CenterClinicId);
        Assert.True(stillActive.IsActive);
        Assert.Null(stillActive.DeactivatedAt);
    }

    [Fact]
    public async Task Link_rejects_inactive_center()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);
        var clinicId = await SeedClinicAsync(db, "CL-1", "عيون");
        var centerId = await SeedCenterAsync(db, "C-OFF", "معطّل", isActive: false);

        var error = await Assert.ThrowsAsync<InactiveMedicalCenterException>(() =>
            service.LinkAsync(centerId, new LinkCenterClinicDto { ClinicId = clinicId }));

        Assert.Equal(400, error.Status);
        Assert.Equal(0, await db.CenterClinics.CountAsync());
    }

    [Fact]
    public async Task Link_rejects_inactive_clinic()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);
        var clinicId = await SeedClinicAsync(db, "CL-OFF", "معطّلة", isActive: false);
        var centerId = await SeedCenterAsync(db, "C-1", "مركز");

        var error = await Assert.ThrowsAsync<InactiveClinicException>(() =>
            service.LinkAsync(centerId, new LinkCenterClinicDto { ClinicId = clinicId }));

        Assert.Equal(400, error.Status);
        Assert.Equal(0, await db.CenterClinics.CountAsync());
    }

    [Fact]
    public async Task Manage_without_permission_is_forbidden()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: false);
        var clinicId = await SeedClinicAsync(db, "CL-1", "عيون");
        var centerId = await SeedCenterAsync(db, "C-1", "مركز");

        var error = Assert.Throws<ForbiddenPermissionException>(() =>
            service.LinkAsync(centerId, new LinkCenterClinicDto { ClinicId = clinicId })
                .GetAwaiter().GetResult());

        Assert.Equal(403, error.Status);
        Assert.Equal(0, await db.CenterClinics.CountAsync());
    }

    [Fact]
    public async Task Operations_on_unknown_center_return_not_found()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);
        var clinicId = await SeedClinicAsync(db, "CL-1", "عيون");

        var error = await Assert.ThrowsAsync<MedicalCenterNotFoundException>(() =>
            service.LinkAsync(Guid.NewGuid(), new LinkCenterClinicDto { ClinicId = clinicId }));

        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task EnsureAcceptsDoctorAssignment_rejects_inactive_link()
    {
        await using var db = CreateDb();
        var (service, centerA, _, clinic) = await SeedTwoCentersOneClinicAsync(db);
        var linked = await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic });
        await service.DeactivateAsync(centerA, linked.CenterClinicId);

        var error = await Assert.ThrowsAsync<InactiveCenterClinicException>(() =>
            service.EnsureAcceptsDoctorAssignmentAsync(centerA, clinic));

        Assert.Equal(400, error.Status);
    }

    [Fact]
    public async Task EnsureAcceptsDoctorAssignment_allows_active_link()
    {
        await using var db = CreateDb();
        var (service, centerA, _, clinic) = await SeedTwoCentersOneClinicAsync(db);
        await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic });

        await service.EnsureAcceptsDoctorAssignmentAsync(centerA, clinic);
    }

    [Fact]
    public async Task Link_reactivate_deactivated_row_instead_of_duplicate()
    {
        await using var db = CreateDb();
        var (service, centerA, _, clinic) = await SeedTwoCentersOneClinicAsync(db);

        var first = await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic });
        await service.DeactivateAsync(centerA, first.CenterClinicId);

        var reactivated = await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic });

        Assert.Equal(first.CenterClinicId, reactivated.CenterClinicId);
        Assert.True(reactivated.IsActive);
        Assert.Null(reactivated.DeactivatedAt);
        Assert.Equal(1, await db.CenterClinics.CountAsync());
    }

    [Fact]
    public async Task Create_activate_deactivate_are_audited()
    {
        await using var db = CreateDb();
        var (service, centerA, _, clinic) = await SeedTwoCentersOneClinicAsync(db);

        var linked = await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic });

        var createAudit = await db.AuditEvents.SingleAsync(e =>
            e.EntityType == nameof(CenterClinic) &&
            e.EntityId == linked.CenterClinicId.ToString() &&
            e.EventType == "Added");
        Assert.Null(createAudit.OldValues);
        Assert.Contains(clinic.ToString(), createAudit.NewValues);
        Assert.Equal("actor-1", createAudit.ActorUserId);

        await service.DeactivateAsync(centerA, linked.CenterClinicId);
        var deactivateAudit = await db.AuditEvents
            .Where(e =>
                e.EntityType == nameof(CenterClinic) &&
                e.EntityId == linked.CenterClinicId.ToString() &&
                e.EventType == "Modified")
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();
        Assert.Contains("\"IsActive\":true", deactivateAudit.OldValues);
        Assert.Contains("\"IsActive\":false", deactivateAudit.NewValues);

        await service.ActivateAsync(centerA, linked.CenterClinicId);
        var activateAudit = await db.AuditEvents
            .Where(e =>
                e.EntityType == nameof(CenterClinic) &&
                e.EntityId == linked.CenterClinicId.ToString() &&
                e.EventType == "Modified")
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();
        Assert.Contains("\"IsActive\":false", activateAudit.OldValues);
        Assert.Contains("\"IsActive\":true", activateAudit.NewValues);
    }

    [Fact]
    public async Task Concurrent_duplicate_link_attempts_leave_single_row()
    {
        await using var db = CreateDb();
        var (service, centerA, _, clinic) = await SeedTwoCentersOneClinicAsync(db);

        await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic });

        // محاكاة سباق: محاولة ثانية بعد وجود السجل تُرفض في الخدمة؛ القيد الفريد في PostgreSQL يغطي السباق الحقيقي.
        await Assert.ThrowsAsync<CenterClinicAlreadyExistsException>(() =>
            service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic }));

        Assert.Equal(1, await db.CenterClinics.CountAsync(x => x.CenterId == centerA && x.ClinicId == clinic));
    }

    [Fact]
    public void Activity_guard_rejects_null_and_inactive()
    {
        Assert.Throws<CenterClinicNotFoundException>(() =>
            CenterClinicActivityGuard.EnsureAcceptsDoctorAssignment(null));

        Assert.Throws<InactiveCenterClinicException>(() =>
            CenterClinicActivityGuard.EnsureAcceptsDoctorAssignment(new CenterClinic
            {
                CenterClinicId = Guid.NewGuid(),
                IsActive = false
            }));

        CenterClinicActivityGuard.EnsureAcceptsDoctorAssignment(new CenterClinic
        {
            CenterClinicId = Guid.NewGuid(),
            IsActive = true
        });
    }

    [Fact]
    public async Task List_by_center_supports_filter_and_pagination()
    {
        await using var db = CreateDb();
        var (service, centerA, _, _) = await SeedTwoCentersOneClinicAsync(db);
        var clinic2 = await SeedClinicAsync(db, "CL-2", "باطنة", sortOrder: 2);
        var link1 = await service.LinkAsync(centerA, new LinkCenterClinicDto
        {
            ClinicId = await db.Clinics.Where(c => c.Code == "CL-1").Select(c => c.ClinicId).SingleAsync()
        });
        await service.LinkAsync(centerA, new LinkCenterClinicDto { ClinicId = clinic2 });
        await service.DeactivateAsync(centerA, link1.CenterClinicId);

        var activeOnly = await service.GetByCenterAsync(centerA, 1, 10, isActive: true);
        Assert.Equal(1, activeOnly.TotalCount);
        Assert.Equal("CL-2", Assert.Single(activeOnly.Items).ClinicCode);
    }

    private static async Task<(ICenterClinicService Service, Guid CenterA, Guid CenterB, Guid ClinicId)>
        SeedTwoCentersOneClinicAsync(AppDbContext db)
    {
        var service = CreateService(db, allowManage: true);
        var clinicId = await SeedClinicAsync(db, "CL-1", "عيون", sortOrder: 1);
        var centerA = await SeedCenterAsync(db, "CA", "مركز أ");
        var centerB = await SeedCenterAsync(db, "CB", "مركز ب");
        return (service, centerA, centerB, clinicId);
    }

    private static async Task<Guid> SeedCenterAsync(AppDbContext db, string code, string nameAr, bool isActive = true)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.MedicalCenters.Add(new MedicalCenter
        {
            CenterId = id,
            Code = code,
            NameAr = nameAr,
            IsActive = isActive,
            DeactivationReason = isActive ? null : "إيقاف",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedClinicAsync(
        AppDbContext db,
        string code,
        string nameAr,
        bool isActive = true,
        short sortOrder = 1)
    {
        var id = Guid.NewGuid();
        db.Clinics.Add(new Clinic
        {
            ClinicId = id,
            Code = code,
            NameAr = nameAr,
            SortOrder = sortOrder,
            CountsForFinal = true,
            IsActive = isActive
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static CenterClinicService CreateService(AppDbContext db, bool allowManage)
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

        return new CenterClinicService(
            new CenterClinicRepository(db),
            new MedicalCenterRepository(db),
            new ReferenceDataRepository(db),
            new TestUnitOfWork(db),
            gate.Object);
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
