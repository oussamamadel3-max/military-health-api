using Application.Common.Permissions;
using Application.Dtos.ReferenceData;
using Application.Common.ReferenceData;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.ReferenceData;
using Application.Services.ReferenceData.Handlers;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class ReferenceDataServiceTests
{
    [Fact]
    public async Task Recruitment_center_rejects_duplicate_code()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        await service.CreateAsync(ReferenceCatalogs.RecruitmentCenters, new ReferenceDataWriteDto
        {
            Code = "RC-1",
            NameAr = "تجنيد أ",
            IsActive = true
        });

        var error = await Assert.ThrowsAsync<ReferenceDataDuplicateException>(() =>
            service.CreateAsync(ReferenceCatalogs.RecruitmentCenters, new ReferenceDataWriteDto
            {
                Code = "rc-1",
                NameAr = "تجنيد ب",
                IsActive = true
            }));

        Assert.Equal(400, error.Status);
        Assert.Equal(1, await db.RecruitmentCenters.CountAsync());
    }

    [Fact]
    public async Task Inactive_values_hidden_from_operational_options_but_visible_in_admin()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        var created = await service.CreateAsync(ReferenceCatalogs.RecruitmentCenters, new ReferenceDataWriteDto
        {
            Code = "RC-2",
            NameAr = "مركز ظاهر",
            IsActive = true
        });

        await service.SetActiveAsync(ReferenceCatalogs.RecruitmentCenters, created.Id, isActive: false);

        var options = await service.GetActiveOptionsAsync(ReferenceCatalogs.RecruitmentCenters);
        Assert.DoesNotContain(options, x => x.Id == created.Id);

        var admin = await service.GetAdminListAsync(ReferenceCatalogs.RecruitmentCenters, 1, 10, isActive: null);
        Assert.Contains(admin.Items, x => x.Id == created.Id && !x.IsActive);
    }

    [Fact]
    public async Task Manage_without_permission_is_forbidden()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: false);

        var error = Assert.Throws<ForbiddenPermissionException>(() =>
            service.CreateAsync(ReferenceCatalogs.MaritalStatuses, new ReferenceDataWriteDto
            {
                Code = "S",
                NameAr = "أعزب",
                IsActive = true
            }).GetAwaiter().GetResult());

        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task Result_status_create_and_update_are_locked()
    {
        await using var db = CreateDb();
        db.ResultStatuses.Add(new ResultStatus
        {
            StatusId = 1,
            Code = "ACCEPTED",
            NameAr = "مقبول",
            IsActive = true,
            IsLocked = true
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, allowManage: true);

        await Assert.ThrowsAsync<ReferenceDataLockedException>(() =>
            service.CreateAsync(ReferenceCatalogs.ResultStatuses, new ReferenceDataWriteDto
            {
                Code = "OTHER",
                NameAr = "أخرى",
                IsActive = true
            }));

        await Assert.ThrowsAsync<ReferenceDataLockedException>(() =>
            service.UpdateAsync(ReferenceCatalogs.ResultStatuses, "1", new ReferenceDataWriteDto
            {
                Code = "ACCEPTED",
                NameAr = "مقبول معدّل",
                IsActive = true
            }));

        await Assert.ThrowsAsync<ReferenceDataLockedException>(() =>
            service.SetActiveAsync(ReferenceCatalogs.ResultStatuses, "1", false));
    }

    [Fact]
    public async Task Create_and_deactivate_are_audited()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        var created = await service.CreateAsync(ReferenceCatalogs.RecruitmentCenters, new ReferenceDataWriteDto
        {
            Code = "AUD-RC",
            NameAr = "تدقيق",
            IsActive = true
        });

        var createAudit = await db.AuditEvents.SingleAsync(e =>
            e.EntityType == nameof(RecruitmentCenter) &&
            e.EntityId == created.Id &&
            e.EventType == "Added");
        Assert.Contains("AUD-RC", createAudit.NewValues);

        await service.SetActiveAsync(ReferenceCatalogs.RecruitmentCenters, created.Id, false);

        var updateAudit = await db.AuditEvents
            .Where(e => e.EntityType == nameof(RecruitmentCenter) && e.EventType == "Modified")
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();
        Assert.Contains("\"IsActive\":true", updateAudit.OldValues);
        Assert.Contains("\"IsActive\":false", updateAudit.NewValues);
    }

    [Fact]
    public async Task Marital_status_rejects_duplicate_name()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        await service.CreateAsync(ReferenceCatalogs.MaritalStatuses, new ReferenceDataWriteDto
        {
            Code = "SINGLE",
            NameAr = "أعزب",
            SortOrder = 1,
            IsActive = true
        });

        await Assert.ThrowsAsync<ReferenceDataDuplicateException>(() =>
            service.CreateAsync(ReferenceCatalogs.MaritalStatuses, new ReferenceDataWriteDto
            {
                Code = "SINGLE-2",
                NameAr = "أعزب",
                SortOrder = 2,
                IsActive = true
            }));
    }

    [Fact]
    public async Task Validation_rule_create_update_and_options_filter_inactive()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        var created = await service.CreateAsync(ReferenceCatalogs.ValidationRules, new ReferenceDataWriteDto
        {
            Code = "HEIGHT",
            NameAr = "الطول",
            TargetEntity = "Member",
            TargetField = "HeightCm",
            MinValue = 100,
            MaxValue = 220,
            VersionNo = 1,
            IsActive = true
        });

        await service.SetActiveAsync(ReferenceCatalogs.ValidationRules, created.Id, false);
        var options = await service.GetActiveOptionsAsync(ReferenceCatalogs.ValidationRules);
        Assert.DoesNotContain(options, x => x.Id == created.Id);
    }

    [Fact]
    public async Task Clinic_create_rejects_reserved_name_and_allows_an_extra_clinic()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);

        var rejected = await Assert.ThrowsAsync<InvalidReferenceDataException>(() =>
            service.CreateAsync(ReferenceCatalogs.Clinics, new ReferenceDataWriteDto
            {
                Code = "ORTHO",
                NameAr = "العظمية",
                SortOrder = 5,
                IsActive = true
            }));
        Assert.Contains("ORTHOPEDIC", rejected.Message, StringComparison.Ordinal);

        var created = await service.CreateAsync(ReferenceCatalogs.Clinics, new ReferenceDataWriteDto
        {
            Code = "CARDIOLOGY",
            NameAr = "عيادة القلب",
            SortOrder = 8,
            IsActive = true
        });

        Assert.Equal("CARDIOLOGY", created.Code);
        Assert.Equal(0, await db.Clinics.CountAsync(clinic => clinic.Code == "ORTHO"));
    }

    [Fact]
    public async Task Clinic_update_rejects_renaming_into_a_reserved_exam_name()
    {
        await using var db = CreateDb();
        var service = CreateService(db, allowManage: true);
        var created = await service.CreateAsync(ReferenceCatalogs.Clinics, new ReferenceDataWriteDto
        {
            Code = "CARDIOLOGY",
            NameAr = "عيادة القلب",
            SortOrder = 8,
            IsActive = true
        });

        var rejected = await Assert.ThrowsAsync<InvalidReferenceDataException>(() =>
            service.UpdateAsync(ReferenceCatalogs.Clinics, created.Id, new ReferenceDataWriteDto
            {
                Code = "CARDIOLOGY",
                NameAr = "عيادة العظمية",
                SortOrder = 8,
                IsActive = true
            }));

        Assert.Contains("ORTHOPEDIC", rejected.Message, StringComparison.Ordinal);
        Assert.Equal("عيادة القلب", (await db.Clinics.SingleAsync()).NameAr);
    }

    private static ReferenceDataService CreateService(AppDbContext db, bool allowManage)
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
                p.Contains(MilitaryHealthPermissions.System.ReferenceData))));
        }

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns("actor-1");
        currentUser.SetupGet(c => c.UserName).Returns("actor");

        var store = new ReferenceDataRepository(db);
        var uow = new TestUnitOfWork(db);
        IReferenceCatalogHandler[] handlers =
        [
            new RecruitmentCenterCatalogHandler(store, uow),
            new ClinicCatalogHandler(store, uow),
            new ResultStatusCatalogHandler(store, uow),
            new RequiredActionTypeCatalogHandler(store, uow),
            new MaritalStatusCatalogHandler(store, uow),
            new BloodTypeCatalogHandler(store, uow),
            new ConsultationTypeCatalogHandler(store, uow),
            new InvestigationTypeCatalogHandler(store, uow),
            new BmiCategoryCatalogHandler(store, uow),
            new ValidationRuleCatalogHandler(store, uow, currentUser.Object)
        ];

        return new ReferenceDataService(handlers, gate.Object, currentUser.Object);
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
