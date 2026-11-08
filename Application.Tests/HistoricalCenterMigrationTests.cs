using Application.Common.Permissions;
using Application.Dtos.Centers;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.Centers;
using Domain.Entities.Audit;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class HistoricalCenterMigrationTests
{
    [Fact]
    public async Task Preview_counts_empty_and_unknown_centers_and_skips_central_and_profile_files()
    {
        await using var db = CreateDb();
        var centerId = await SeedCenterAsync(db, "CTR-A", active: true);
        var orphan = MemberRow(current: Guid.Empty, queue: Guid.NewGuid());
        var linked = MemberRow(current: centerId, queue: centerId);
        db.Members.AddRange(orphan, linked);
        db.FileAssets.Add(ProfileFile(centerId: null));
        db.FileAssets.Add(ProfileFile(centerId: Guid.Empty));
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ContextScope = "CENTRAL",
            CenterId = null,
            EventType = "Sample",
            Result = "SUCCESS",
            CorrelationId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var report = await CreateMigration(db, allow: true).PreviewAsync();

        Assert.Equal(1, Count(report, "Member"));
        Assert.Equal(1, Count(report, "FileAsset"));
        Assert.DoesNotContain(report.Entities, e => e.EntityName == nameof(AuditEvent));
        Assert.Contains(orphan.MemberId, report.Entities.Single(e => e.EntityName == "Member").SampleIds);
        Assert.False(report.MigrationVerified);
    }

    [Fact]
    public async Task Execute_assigns_invalid_center_refs_and_keeps_a_valid_center()
    {
        await using var db = CreateDb();
        var centerId = await SeedCenterAsync(db, "CTR-B", active: true);
        var orphan = MemberRow(current: Guid.Empty, queue: centerId);
        var profile = ProfileFile(centerId: null);
        var brokenFile = ProfileFile(centerId: Guid.Empty);
        db.Members.Add(orphan);
        db.FileAssets.AddRange(profile, brokenFile);
        await db.SaveChangesAsync();

        var report = await CreateMigration(db, allow: true).ExecuteAsync(centerId);

        Assert.Equal(centerId, report.TargetCenterId);
        Assert.Equal(1, report.Entities.Single(e => e.EntityName == "Member").AssignedCount);
        Assert.Equal(1, report.Entities.Single(e => e.EntityName == "FileAsset").AssignedCount);
        Assert.False(report.MigrationVerified);

        var saved = await db.Members.AsNoTracking().SingleAsync();
        Assert.Equal(centerId, saved.CurrentMedicalCenterId);
        Assert.Equal(centerId, saved.QueueMedicalCenterId);
        Assert.Null((await db.FileAssets.AsNoTracking().SingleAsync(f => f.FileAssetId == profile.FileAssetId)).CenterId);
        Assert.Equal(centerId, (await db.FileAssets.AsNoTracking().SingleAsync(f => f.FileAssetId == brokenFile.FileAssetId)).CenterId);

        var audit = await db.AuditEvents.SingleAsync(e => e.EventType == HistoricalCenterMigrationService.ExecutedEventType);
        Assert.Equal("CENTRAL", audit.ContextScope);
        Assert.Null(audit.CenterId);
        Assert.Contains(centerId.ToString(), audit.NewValues);
        Assert.Empty(db.HistoricalCenterMigrationCompletions);
    }

    [Fact]
    public async Task Execute_stops_without_assignment_when_a_transfer_cannot_be_attributed()
    {
        await using var db = CreateDb();
        var centerId = await SeedCenterAsync(db, "CTR-C", active: true);
        var orphan = MemberRow(current: Guid.Empty, queue: Guid.Empty);
        db.Members.Add(orphan);
        db.MemberTransfers.Add(new MemberTransfer
        {
            TransferId = Guid.NewGuid(),
            MemberId = orphan.MemberId,
            FromCenterId = Guid.Empty,
            ToCenterId = Guid.NewGuid(),
            FromMemberCenterIdentityId = Guid.NewGuid(),
            ToMemberCenterIdentityId = Guid.NewGuid(),
            TransferReason = "سبب",
            ExecutedByUserId = "actor-1",
            ExecutedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<HistoricalMigrationBlockedException>(() =>
            CreateMigration(db, allow: true).ExecuteAsync(centerId));

        Assert.Equal(409, error.Status);
        Assert.True(((HistoricalMigrationReportDto)error.Meta!).TotalUnassignableCount > 0);
        var saved = await db.Members.AsNoTracking().SingleAsync();
        Assert.Equal(Guid.Empty, saved.CurrentMedicalCenterId);
        Assert.False(await db.AuditEvents.AnyAsync(e => e.EventType == HistoricalCenterMigrationService.ExecutedEventType));
    }

    [Fact]
    public async Task Failed_save_does_not_keep_assigned_centers()
    {
        await using var db = CreateDb();
        var centerId = await SeedCenterAsync(db, "CTR-D", active: true);
        var orphan = MemberRow(current: Guid.Empty, queue: Guid.Empty);
        db.Members.Add(orphan);
        await db.SaveChangesAsync();

        var migration = CreateMigration(db, allow: true, new ThrowOnSaveUnitOfWork(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => migration.ExecuteAsync(centerId));

        var saved = await db.Members.AsNoTracking().SingleAsync();
        Assert.Equal(Guid.Empty, saved.CurrentMedicalCenterId);
        Assert.Empty(db.HistoricalCenterMigrationCompletions);
    }

    [Fact]
    public async Task Verify_rejects_remaining_orphans_and_records_completion_only_when_clear()
    {
        await using var db = CreateDb();
        var centerId = await SeedCenterAsync(db, "CTR-E", active: true);
        db.Members.Add(MemberRow(current: Guid.Empty, queue: centerId));
        await db.SaveChangesAsync();
        var migration = CreateMigration(db, allow: true);

        var rejected = await Assert.ThrowsAsync<HistoricalMigrationVerificationFailedException>(() =>
            migration.VerifyAsync(centerId));
        Assert.Equal(409, rejected.Status);
        Assert.Empty(db.HistoricalCenterMigrationCompletions);

        await migration.ExecuteAsync(centerId);
        var status = await migration.GetStatusAsync();
        Assert.False(status.MigrationVerified);

        var verified = await migration.VerifyAsync(centerId);
        Assert.True(verified.MigrationVerified);
        Assert.False(verified.PreExistingMultiCenterWithoutVerification);
        Assert.Equal(centerId, verified.Completion!.TargetCenterId);
        Assert.False(verified.Completion.PreExistingMultiCenterDetected);
        Assert.Equal(1, await db.HistoricalCenterMigrationCompletions.CountAsync());
        Assert.True(await db.AuditEvents.AnyAsync(e => e.EventType == HistoricalCenterMigrationService.VerifiedEventType));

        var again = await migration.VerifyAsync(centerId);
        Assert.Equal(verified.Completion.Id, again.Completion!.Id);
        Assert.Equal(1, await db.HistoricalCenterMigrationCompletions.CountAsync());
    }

    [Fact]
    public async Task Second_active_center_is_blocked_until_verification()
    {
        await using var db = CreateDb();
        var gate = AllowGate();
        var centers = CreateCenters(db, gate);
        var migration = CreateMigration(db, allow: true, gate: gate);

        var first = await centers.CreateAsync(new CreateMedicalCenterDto
        {
            Code = "ONE",
            NameAr = "الأول",
            IsActive = true
        });

        var blocked = await Assert.ThrowsAsync<MultiCenterActivationBlockedException>(() =>
            centers.CreateAsync(new CreateMedicalCenterDto
            {
                Code = "TWO",
                NameAr = "الثاني",
                IsActive = true
            }));
        Assert.Equal(409, blocked.Status);

        var inactive = await centers.CreateAsync(new CreateMedicalCenterDto
        {
            Code = "TWO",
            NameAr = "الثاني",
            IsActive = false,
            DeactivationReason = "بانتظار التحقق"
        });
        await Assert.ThrowsAsync<MultiCenterActivationBlockedException>(() =>
            centers.ActivateAsync(inactive.CenterId));

        await migration.VerifyAsync(first.CenterId);
        var activated = await centers.ActivateAsync(inactive.CenterId);
        Assert.True(activated.IsActive);
        Assert.Equal(2, await db.MedicalCenters.CountAsync(c => c.IsActive));
    }

    [Fact]
    public async Task Pre_existing_active_centers_are_reported_and_not_deactivated()
    {
        await using var db = CreateDb();
        var first = await SeedCenterAsync(db, "OLD-1", active: true);
        var second = await SeedCenterAsync(db, "OLD-2", active: true);
        var gate = AllowGate();
        var migration = CreateMigration(db, allow: true, gate: gate);
        var centers = CreateCenters(db, gate);

        var before = await migration.GetStatusAsync();
        Assert.False(before.MigrationVerified);
        Assert.True(before.PreExistingMultiCenterWithoutVerification);
        Assert.Equal(2, before.ActiveCenterCount);

        await Assert.ThrowsAsync<MultiCenterActivationBlockedException>(() =>
            centers.CreateAsync(new CreateMedicalCenterDto
            {
                Code = "OLD-3",
                NameAr = "ثالث",
                IsActive = true
            }));

        var verified = await migration.VerifyAsync(first);
        Assert.True(verified.MigrationVerified);
        Assert.False(verified.PreExistingMultiCenterWithoutVerification);
        Assert.True(verified.Completion!.PreExistingMultiCenterDetected);
        Assert.Equal(2, await db.MedicalCenters.CountAsync(c => c.IsActive));
        Assert.Equal(second, (await db.MedicalCenters.AsNoTracking().SingleAsync(c => c.Code == "OLD-2")).CenterId);

        var third = await centers.CreateAsync(new CreateMedicalCenterDto
        {
            Code = "OLD-3",
            NameAr = "ثالث",
            IsActive = true
        });
        Assert.True(third.IsActive);
    }

    [Fact]
    public async Task Preview_without_permission_is_forbidden()
    {
        await using var db = CreateDb();
        var error = await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            CreateMigration(db, allow: false).PreviewAsync());
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task Execute_rejects_inactive_target_center()
    {
        await using var db = CreateDb();
        var centerId = await SeedCenterAsync(db, "OFF", active: false);
        var error = await Assert.ThrowsAsync<HistoricalMigrationTargetInvalidException>(() =>
            CreateMigration(db, allow: true).ExecuteAsync(centerId));
        Assert.Equal(400, error.Status);
    }

    private static int Count(HistoricalMigrationReportDto report, string entity)
        => report.Entities.Single(e => e.EntityName == entity).OrphanCount;

    private static HistoricalCenterMigrationService CreateMigration(
        AppDbContext db,
        bool allow,
        IUnitOfWork? unitOfWork = null,
        IPermissionGate? gate = null)
        => new(
            new HistoricalCenterMigrationRepository(db),
            unitOfWork ?? new DbUnitOfWork(db),
            new AuditLogRepository(db),
            new TestCurrentUser(),
            gate ?? Gate(allow));

    private static MedicalCenterService CreateCenters(AppDbContext db, IPermissionGate gate)
    {
        var config = new Mapster.TypeAdapterConfig();
        new Application.Mappings.MedicalCenterProfile().Register(config);
        return new MedicalCenterService(
            new MedicalCenterRepository(db),
            new DbUnitOfWork(db),
            new MapsterMapper.Mapper(config),
            gate,
            CreateMigration(db, allow: true, gate: gate));
    }

    private static IPermissionGate AllowGate() => Gate(true);

    private static IPermissionGate Gate(bool allow)
    {
        var gate = new Mock<IPermissionGate>();
        if (!allow)
        {
            gate.Setup(g => g.EnsureAll(It.IsAny<string[]>()))
                .Throws(new ForbiddenPermissionException());
        }
        else
        {
            gate.Setup(g => g.EnsureAll(It.Is<string[]>(p =>
                p.Contains(MilitaryHealthPermissions.Centers.Manage))));
        }
        return gate.Object;
    }

    private static async Task<Guid> SeedCenterAsync(AppDbContext db, string code, bool active)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.MedicalCenters.Add(new MedicalCenter
        {
            CenterId = id,
            Code = code,
            NameAr = code,
            IsActive = active,
            DeactivationReason = active ? null : "موقوف",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static Member MemberRow(Guid current, Guid queue)
    {
        var now = DateTimeOffset.UtcNow;
        return new Member
        {
            MemberId = Guid.NewGuid(),
            FileNumber = Random.Shared.NextInt64(1, 100000),
            RecruitmentNumber = Guid.NewGuid().ToString("N"),
            FullName = "منتسب",
            MotherName = "أم",
            BirthDate = new DateOnly(2000, 1, 1),
            MaritalStatusId = 1,
            Occupation = "عمل",
            RecruitmentCenterId = Guid.NewGuid(),
            CurrentMedicalCenterId = current,
            QueueMedicalCenterId = queue,
            QueueDate = new DateOnly(2026, 1, 1),
            QueueNumber = 1,
            ExportStatus = "UNEXPORTED",
            CreatedByUserId = "actor-1",
            UpdatedByUserId = "actor-1",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static FileAsset ProfileFile(Guid? centerId) => new()
    {
        FileAssetId = Guid.NewGuid(),
        CenterId = centerId,
        StorageKey = Guid.NewGuid().ToString("N"),
        OriginalName = "photo.png",
        MimeType = "image/png",
        Extension = ".png",
        SizeBytes = 10,
        UploadedByUserId = "actor-1",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static AppDbContext CreateDb()
    {
        var user = new TestCurrentUser();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, user, new TestCenter(null, true), new AuditInterceptor(user));
    }

    private sealed class DbUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public DbUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => _db.Database.BeginTransactionAsync(cancellationToken);
    }

    private sealed class ThrowOnSaveUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public ThrowOnSaveUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("forced save failure");
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => _db.Database.BeginTransactionAsync(cancellationToken);
    }
}
