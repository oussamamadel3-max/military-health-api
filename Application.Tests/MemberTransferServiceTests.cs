using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.Members;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Patient;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class MemberTransferServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CenterInactive = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task TransferLocal_closes_identity_opens_new_updates_center_keeps_file_and_clinical()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var member = await CreateMemberAsync(db, CenterA, fileNumber: 42, recruitment: "TR-1");

        var examId = Guid.NewGuid();
        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = examId,
            MemberId = member.MemberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            ExaminerUserId = "actor-1",
            ExaminedAt = DateTimeOffset.UtcNow,
            ExamSequence = 1,
            IsCurrent = true,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, CenterA, allowTransfer: true, allowView: true);
        var result = await service.TransferLocalAsync(member.MemberId, new TransferMemberRequest
        {
            ToCenterId = CenterB,
            TransferReason = "إعادة توزيع أحمال المراكز"
        });

        Assert.Equal(42, result.FileNumber);
        Assert.Equal(CenterA, result.FromCenterId);
        Assert.Equal(CenterB, result.ToCenterId);
        Assert.Equal("إعادة توزيع أحمال المراكز", result.TransferReason);
        Assert.Equal(2, result.DataRevision);
        Assert.Equal(1, result.QueueNumber); // preserved reception queue

        var row = await db.Members.SingleAsync(m => m.MemberId == member.MemberId);
        Assert.Equal(42, row.FileNumber);
        Assert.Equal(CenterB, row.CurrentMedicalCenterId);
        Assert.Equal(CenterA, row.QueueMedicalCenterId); // historical queue origin immutable
        Assert.Equal(2, row.DataRevision);

        var identities = await db.MemberCenterIdentities
            .Where(i => i.MemberId == member.MemberId)
            .OrderBy(i => i.ValidFrom)
            .ToListAsync();
        Assert.Equal(2, identities.Count);
        Assert.NotNull(identities[0].ValidTo);
        Assert.Equal("actor-1", identities[0].ClosedByUserId);
        Assert.Equal(CenterA, identities[0].MedicalCenterId);
        Assert.Null(identities[1].ValidTo);
        Assert.Equal(CenterB, identities[1].MedicalCenterId);
        Assert.Equal("42", identities[1].CenterMemberId);

        Assert.Equal(1, await db.MemberTransfers.CountAsync(t => t.MemberId == member.MemberId));
        Assert.Equal(1, await db.MedicalExams.CountAsync(e => e.ExamId == examId && e.MemberId == member.MemberId));

        var audit = await db.AuditEvents.SingleAsync(e => e.EventType == MemberTransferService.TransferExecutedEventType);
        Assert.Equal("SUCCESS", audit.Result);
        Assert.Equal(nameof(Member), audit.EntityType);
        Assert.Equal(member.MemberId.ToString(), audit.EntityId);
        Assert.Equal(CenterA, audit.CenterId);
    }

    [Fact]
    public async Task TransferLocal_rejects_same_center_missing_reason_inactive_target_and_unauthorized()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var member = await CreateMemberAsync(db, CenterA, fileNumber: 7, recruitment: "TR-NEG");

        var allowed = CreateService(db, CenterA, allowTransfer: true);

        await Assert.ThrowsAsync<MemberTransferSameCenterException>(() =>
            allowed.TransferLocalAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterA,
                TransferReason = "سبب"
            }));

        await Assert.ThrowsAsync<InvalidMemberTransferDataException>(() =>
            allowed.TransferLocalAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "   "
            }));

        await Assert.ThrowsAsync<InactiveMedicalCenterException>(() =>
            allowed.TransferLocalAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterInactive,
                TransferReason = "نقل لمركز معطّل"
            }));

        var denied = CreateService(db, CenterA, allowTransfer: false);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            denied.TransferLocalAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "محاولة بلا صلاحية"
            }));

        Assert.Equal(0, await db.MemberTransfers.CountAsync());
        Assert.Equal(CenterA, (await db.Members.SingleAsync(m => m.MemberId == member.MemberId)).CurrentMedicalCenterId);
    }

    [Fact]
    public async Task TransferLocal_blocks_open_deferred_and_edit_lock()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var member = await CreateMemberAsync(db, CenterA, fileNumber: 8, recruitment: "TR-ELIG");

        db.DeferredActions.Add(new DeferredAction
        {
            DeferredActionId = Guid.NewGuid(),
            MemberId = member.MemberId,
            CenterId = CenterA,
            DeferReason = "مؤجل",
            ActionTypeId = 1,
            FollowupNote = "متابعة",
            Status = MemberTransferService.DeferredRequestedStatus,
            RequestedByUserId = "actor-1",
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, CenterA, allowTransfer: true);
        await Assert.ThrowsAsync<MemberTransferNotEligibleException>(() =>
            service.TransferLocalAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "نقل مع مؤجل مفتوح"
            }));

        db.DeferredActions.RemoveRange(db.DeferredActions);
        db.ClinicExamEditLocks.Add(new ClinicExamEditLock
        {
            LockId = Guid.NewGuid(),
            MemberId = member.MemberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            LockedByUserId = "actor-1",
            LockedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<MemberTransferNotEligibleException>(() =>
            service.TransferLocalAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "نقل مع قفل تحرير"
            }));

        Assert.Equal(0, await db.MemberTransfers.CountAsync());
    }

    [Fact]
    public async Task TransferLocal_allows_exported_member_and_resets_export_per_db_function()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var member = await CreateMemberAsync(db, CenterA, fileNumber: 9, recruitment: "TR-EXP");
        member.ExportStatus = "EXPORTED";
        member.LastExportedAt = DateTimeOffset.UtcNow;
        member.LastExportedRevision = 1;
        await db.SaveChangesAsync();

        var result = await CreateService(db, CenterA, allowTransfer: true)
            .TransferLocalAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "نقل ملف مصدَّر"
            });

        Assert.Equal("EXPORTED", result.ExportStatusAtTransfer);
        Assert.Contains(result.NoticesAr, n => n.Contains("غير مصدَّر", StringComparison.Ordinal));
        var row = await db.Members.SingleAsync(m => m.MemberId == member.MemberId);
        Assert.Equal("UNEXPORTED", row.ExportStatus);
        Assert.Null(row.LastExportedAt);
    }

    [Fact]
    public async Task Transfer_prevents_duplicate_from_source_and_revision_conflict()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var member = await CreateMemberAsync(db, CenterA, fileNumber: 10, recruitment: "TR-DUP");

        var fromA = CreateService(db, CenterA, allowTransfer: true, allowView: true);
        var first = await fromA.TransferLocalAsync(member.MemberId, new TransferMemberRequest
        {
            ToCenterId = CenterB,
            TransferReason = "نقل أول",
            ExpectedDataRevision = 1
        });
        Assert.Equal(2, first.DataRevision);

        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            fromA.TransferLocalAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "نقل مكرر من المصدر"
            }));

        var central = CreateService(db, CenterB, allowTransfer: true, allowTransferCentral: true, allowView: true);
        await Assert.ThrowsAsync<MemberTransferConflictException>(() =>
            central.TransferCentralAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterA,
                TransferReason = "مراجعة قديمة",
                ExpectedDataRevision = 1
            }));

        Assert.Equal(1, await db.MemberTransfers.CountAsync());
    }

    [Fact]
    public async Task After_transfer_source_center_loses_local_access_and_target_sees_history()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var member = await CreateMemberAsync(db, CenterA, fileNumber: 11, recruitment: "TR-ISO");

        await CreateService(db, CenterA, allowTransfer: true, allowView: true)
            .TransferLocalAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "عزل بعد النقل"
            });

        var sourceView = CreateService(db, CenterA, allowTransfer: false, allowView: true);
        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            sourceView.GetHistoryLocalAsync(member.MemberId));

        var targetHistory = await CreateService(db, CenterB, allowView: true)
            .GetHistoryLocalAsync(member.MemberId);

        Assert.True(targetHistory.HasTransfers);
        Assert.Equal(CenterB, targetHistory.CurrentMedicalCenterId);
        var item = Assert.Single(targetHistory.Items);
        Assert.Equal(CenterA, item.FromCenterId);
        Assert.Equal(CenterB, item.ToCenterId);
        Assert.Equal("عزل بعد النقل", item.TransferReason);
        Assert.Equal("actor-1", item.ExecutedByUserId);
    }

    [Fact]
    public async Task History_empty_message_and_central_records_cross_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var member = await CreateMemberAsync(db, CenterA, fileNumber: 12, recruitment: "TR-HIST");

        var empty = await CreateService(db, CenterA, allowView: true)
            .GetHistoryLocalAsync(member.MemberId);
        Assert.False(empty.HasTransfers);
        Assert.Contains("لم يُنقل", empty.EmptyMessageAr);

        var cross = new Mock<ICrossCenterAccessService>();
        cross.Setup(c => c.RecordAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var central = CreateService(
            db,
            CenterA,
            allowView: true,
            allowViewCentral: true,
            crossCenter: cross.Object);

        await Assert.ThrowsAsync<InvalidMemberTransferDataException>(() =>
            central.GetHistoryCentralAsync(member.MemberId, "  "));

        var history = await central.GetHistoryCentralAsync(member.MemberId, "مراجعة تدقيق");
        Assert.False(history.HasTransfers);
        cross.Verify(c => c.RecordAsync(
            CenterA,
            "مراجعة تدقيق",
            MemberTransferService.CrossCenterDataType,
            It.IsAny<string?>(),
            nameof(Member),
            member.MemberId.ToString(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransferCentral_moves_member_without_local_source_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var member = await CreateMemberAsync(db, CenterA, fileNumber: 13, recruitment: "TR-CEN");

        var result = await CreateService(db, CenterA, allowTransferCentral: true)
            .TransferCentralAsync(member.MemberId, new TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "نقل مركزي"
            });

        Assert.Equal(CenterB, result.ToCenterId);
        Assert.Equal(CenterB, (await db.Members.SingleAsync(m => m.MemberId == member.MemberId)).CurrentMedicalCenterId);
    }

    #region Fixtures

    private static MemberTransferService CreateService(
        AppDbContext db,
        Guid activeCenter,
        bool allowTransfer = false,
        bool allowTransferCentral = false,
        bool allowView = false,
        bool allowViewCentral = false,
        ICrossCenterAccessService? crossCenter = null)
    {
        var gate = new Mock<IPermissionGate>();

        if (allowTransfer)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.Transfer,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeCenter);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.Transfer,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        if (allowTransferCentral)
        {
            gate.Setup(g => g.EnsureCentralAsync(
                    MilitaryHealthPermissions.Members.Transfer,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        else
        {
            gate.Setup(g => g.EnsureCentralAsync(
                    MilitaryHealthPermissions.Members.Transfer,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        if (allowView)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.View,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeCenter);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Members.View,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        if (allowViewCentral)
        {
            gate.Setup(g => g.EnsureCentralAsync(
                    MilitaryHealthPermissions.Members.ViewCentral,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        else
        {
            gate.Setup(g => g.EnsureCentralAsync(
                    MilitaryHealthPermissions.Members.ViewCentral,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        return new MemberTransferService(
            gate.Object,
            new TestCurrentUser(),
            new MemberRepository(db),
            new MedicalCenterRepository(db),
            new ClinicWorkspaceRepository(db),
            new AuditLogRepository(db),
            crossCenter ?? Mock.Of<ICrossCenterAccessService>(),
            new TransferTestUnitOfWork(db));
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);

        if (!await db.MedicalCenters.AnyAsync(c => c.CenterId == CenterInactive))
        {
            var now = DateTimeOffset.UtcNow;
            db.MedicalCenters.Add(new MedicalCenter
            {
                CenterId = CenterInactive,
                Code = "X",
                NameAr = "مركز معطّل",
                IsActive = false,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (!await db.Clinics.AnyAsync(c => c.ClinicId == ClinicEyes))
        {
            db.Clinics.Add(new Clinic
            {
                ClinicId = ClinicEyes,
                Code = "EYE",
                NameAr = "عيون",
                CountsForFinal = true,
                IsActive = true,
                SortOrder = 1
            });
        }

        if (!await db.RequiredActionTypes.AnyAsync(t => t.ActionTypeId == 1))
        {
            db.RequiredActionTypes.Add(new RequiredActionType
            {
                ActionTypeId = 1,
                Code = "LAB",
                NameAr = "مخبر",
                RequiresDescription = false,
                RequiresTargetClinic = false,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Member> CreateMemberAsync(
        AppDbContext db,
        Guid centerId,
        long fileNumber,
        string recruitment)
    {
        var now = DateTimeOffset.UtcNow;
        var memberId = Guid.NewGuid();
        var member = new Member
        {
            MemberId = memberId,
            FileNumber = fileNumber,
            RecruitmentNumber = recruitment,
            FullName = $"منتسب {recruitment}",
            MotherName = "أم",
            BirthDate = new DateOnly(2000, 1, 1),
            MaritalStatusId = 1,
            Occupation = "طالب",
            RecruitmentCenterId = RecruitmentCenterId,
            BloodTypeId = 1,
            HeightCm = 175,
            WeightKg = 70,
            CurrentMedicalCenterId = centerId,
            QueueMedicalCenterId = centerId,
            QueueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            QueueNumber = 1,
            ExportStatus = "UNEXPORTED",
            DataRevision = 1,
            CreatedByUserId = "actor-1",
            UpdatedByUserId = "actor-1",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Members.Add(member);
        db.MemberCenterIdentities.Add(new MemberCenterIdentity
        {
            MemberCenterIdentityId = Guid.NewGuid(),
            MemberId = memberId,
            MedicalCenterId = centerId,
            CenterMemberId = fileNumber.ToString(),
            ValidFrom = now,
            ValidTo = null,
            AssignedByUserId = "actor-1",
            ClosedByUserId = null
        });
        await db.SaveChangesAsync();
        return member;
    }

    private sealed class TransferTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public TransferTestUnitOfWork(AppDbContext db) => _db = db;
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

    #endregion
}
