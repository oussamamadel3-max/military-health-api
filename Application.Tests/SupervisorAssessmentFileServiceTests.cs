using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Mappings;
using Application.Services.ClinicWorkspace;
using Application.Services.Supervisor;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Domain.Entities.Medical;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class SupervisorAssessmentFileServiceTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClinicInternal = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ClinicSurgery = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ClinicOrtho = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly Guid[] AllClinics =
    [
        ClinicEyes, ClinicEnt, ClinicInternal, ClinicSurgery, ClinicOrtho
    ];

    [Fact]
    public async Task Get_rejects_missing_FinalDecision_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "SUP-PERM");
        var service = CreateService(db, CenterA, allowFinalDecision: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetAssessmentFileAsync(memberId));
    }

    [Fact]
    public async Task Get_rejects_member_from_other_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "SUP-XCTR");
        await SeedAssessmentAsync(db, memberId, CenterA, ClinicEyes, resultStatusId: 1);

        var serviceB = CreateService(db, CenterB, allowFinalDecision: true);

        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            serviceB.GetAssessmentFileAsync(memberId));
    }

    [Fact]
    public async Task Get_incomplete_clinics_flags_missing_and_blocks_decision()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "SUP-INC");
        await SeedAssessmentAsync(db, memberId, CenterA, ClinicEyes, resultStatusId: 1);
        await SeedAssessmentAsync(db, memberId, CenterA, ClinicEnt, resultStatusId: 2, rejectionReason: "سبب");

        var service = CreateService(db, CenterA, allowFinalDecision: true);
        var result = await service.GetAssessmentFileAsync(memberId);

        Assert.Equal(5, result.ClinicAssessments.Count);
        Assert.Equal(2, result.ClinicAssessments.Count(r => !r.IsMissing));
        Assert.Equal(3, result.ClinicAssessments.Count(r => r.IsMissing));
        Assert.False(result.AreMandatoryClinicsComplete);
        Assert.False(result.CanIssueFinalDecision);
        Assert.Equal(SupervisorAssessmentFileService.IncompleteClinicsBlockReasonAr, result.BlockReasonAr);
        Assert.Contains("INTERNAL", result.MissingClinicCodes);
        Assert.Contains("SURGERY", result.MissingClinicCodes);
        Assert.Contains("ORTHOPEDIC", result.MissingClinicCodes);

        var eyes = Assert.Single(result.ClinicAssessments, r => r.ClinicId == ClinicEyes);
        Assert.False(eyes.IsMissing);
        Assert.Equal((short)1, eyes.ResultStatusId);
        Assert.Equal("مقبول", eyes.ResultStatusNameAr);
        Assert.Equal("actor-1", eyes.AssessedByUserId);
        Assert.NotNull(eyes.AssessedAt);

        var missing = Assert.Single(result.ClinicAssessments, r => r.ClinicCode == "INTERNAL");
        Assert.True(missing.IsMissing);
        Assert.Null(missing.AssessmentId);
    }

    [Fact]
    public async Task Get_complete_five_clinics_allows_final_decision()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "SUP-OK");
        foreach (var clinicId in AllClinics)
            await SeedAssessmentAsync(db, memberId, CenterA, clinicId, resultStatusId: 1);

        var service = CreateService(db, CenterA, allowFinalDecision: true);
        var result = await service.GetAssessmentFileAsync(memberId);

        Assert.True(result.AreMandatoryClinicsComplete);
        Assert.True(result.CanIssueFinalDecision);
        Assert.Null(result.BlockReasonAr);
        Assert.Empty(result.MissingClinicCodes);
        Assert.All(result.ClinicAssessments, r => Assert.False(r.IsMissing));
        Assert.Equal(memberId, result.Member.MemberId);
        Assert.Equal(CenterA, result.CenterId);
        Assert.True(result.MedicalRequests.IsEmpty);
        Assert.Equal(1, await db.AuditEvents.CountAsync(e =>
            e.EventType == SupervisorAssessmentFileService.AssessmentFileViewEventType
            && e.Result == "SUCCESS"));
    }

    [Fact]
    public async Task Get_embeds_medical_requests_and_attachment_metadata_without_storage_key()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "SUP-MED");
        foreach (var clinicId in AllClinics)
            await SeedAssessmentAsync(db, memberId, CenterA, clinicId, resultStatusId: 1);

        var consultationId = await SeedConsultationAsync(db, memberId, CenterA, ClinicEyes, "COMPLETED", "سبب", "نتيجة");
        await SeedInvestigationAsync(db, memberId, CenterA, ClinicEnt, "PENDING", "CBC", "مخبري");
        var fileId = await SeedAttachmentAsync(db, memberId, CenterA, consultationId: consultationId);

        var service = CreateService(db, CenterA, allowFinalDecision: true, allowAttachments: true);
        var result = await service.GetAssessmentFileAsync(memberId);

        Assert.IsType<Application.Dtos.ClinicWorkspace.MemberMedicalRequestsDto>(result.MedicalRequests);
        Assert.False(result.MedicalRequests.IsEmpty);
        Assert.Equal(2, result.MedicalRequests.TotalCount);
        Assert.Equal(1, result.MedicalRequests.PendingCount);
        Assert.True(result.HasPendingInvestigations);
        Assert.Contains(SupervisorAssessmentFileService.PendingInvestigationWarningAr, result.WarningsAr);
        Assert.True(result.CanIssueFinalDecision);

        var consultation = Assert.Single(result.MedicalRequests.Items, i =>
            i.RequestKind == MemberMedicalRequestService.RequestKindConsultation);
        var attachment = Assert.Single(consultation.Attachments);
        Assert.Equal(fileId, attachment.FileAssetId);
        Assert.Equal($"/api/Attachments/download/{fileId}", attachment.DownloadPath);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("private/secret", json);
        Assert.DoesNotContain("StorageKey", json);
    }

    [Fact]
    public async Task Get_omits_attachments_when_AttachmentsRead_missing()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "SUP-NOATT");
        foreach (var clinicId in AllClinics)
            await SeedAssessmentAsync(db, memberId, CenterA, clinicId, resultStatusId: 1);

        var consultationId = await SeedConsultationAsync(db, memberId, CenterA, ClinicEyes, "COMPLETED", "سبب", "نتيجة");
        await SeedAttachmentAsync(db, memberId, CenterA, consultationId: consultationId);

        var service = CreateService(db, CenterA, allowFinalDecision: true, allowAttachments: false);
        var result = await service.GetAssessmentFileAsync(memberId);

        Assert.False(result.MedicalRequests.AttachmentsIncluded);
        Assert.Empty(Assert.Single(result.MedicalRequests.Items).Attachments);
    }

    [Fact]
    public async Task Get_returns_general_note_and_deferred_actions()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "SUP-NOTE");
        var assessmentId = await SeedAssessmentAsync(db, memberId, CenterA, ClinicEyes, resultStatusId: 3);
        foreach (var clinicId in AllClinics.Where(id => id != ClinicEyes))
            await SeedAssessmentAsync(db, memberId, CenterA, clinicId, resultStatusId: 1);

        await SeedGeneralNoteAsync(db, memberId, ClinicEyes, "تنبيه طبي هام");
        await SeedDeferredActionAsync(db, memberId, CenterA, ClinicEyes, assessmentId);

        var service = CreateService(db, CenterA, allowFinalDecision: true);
        var result = await service.GetAssessmentFileAsync(memberId);

        Assert.True(result.HasGeneralNotesAlert);
        Assert.Equal("تنبيه طبي هام", result.LatestGeneralNote?.NoteText);
        Assert.True(result.HasOpenDeferredActions);
        Assert.Contains(SupervisorAssessmentFileService.OpenDeferredWarningAr, result.WarningsAr);
        Assert.Single(result.DeferredActions);

        var eyes = Assert.Single(result.ClinicAssessments, r => r.ClinicId == ClinicEyes);
        Assert.Equal((short)3, eyes.ResultStatusId);
        Assert.NotNull(eyes.DeferredAction);
        Assert.Equal("REQUESTED", eyes.DeferredAction!.Status);
        Assert.True(result.CanIssueFinalDecision);
    }

    [Fact]
    public async Task Get_existing_final_decision_blocks_new_issue()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "SUP-DEC");
        foreach (var clinicId in AllClinics)
            await SeedAssessmentAsync(db, memberId, CenterA, clinicId, resultStatusId: 1);

        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = CenterA,
            SupervisorUserId = "actor-1",
            ResultStatusId = 1,
            DecidedAt = DateTimeOffset.UtcNow,
            UpdatedByUserId = "actor-1",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, CenterA, allowFinalDecision: true);
        var result = await service.GetAssessmentFileAsync(memberId);

        Assert.True(result.AreMandatoryClinicsComplete);
        Assert.False(result.CanIssueFinalDecision);
        Assert.Equal(SupervisorAssessmentFileService.ExistingDecisionBlockReasonAr, result.BlockReasonAr);
        Assert.NotNull(result.ExistingFinalDecision);
        Assert.Equal(1, result.ExistingFinalDecision!.ResultStatusId);
    }

    [Fact]
    public async Task Get_rejects_empty_member_id()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, allowFinalDecision: true);

        await Assert.ThrowsAsync<InvalidUserDataException>(() =>
            service.GetAssessmentFileAsync(Guid.Empty));
    }

    [Fact]
    public void Completeness_helper_reports_missing_codes()
    {
        var mandatory = new List<(Guid ClinicId, string Code)>
        {
            (ClinicEyes, "EYE"),
            (ClinicEnt, "ENT"),
            (ClinicInternal, "INTERNAL")
        };
        var assessed = new HashSet<Guid> { ClinicEyes };

        var missing = MandatoryClinicCompleteness.GetMissingClinicCodes(mandatory, assessed);

        Assert.Equal(new[] { "ENT", "INTERNAL" }, missing);
        Assert.False(MandatoryClinicCompleteness.AreComplete(mandatory, assessed));
        Assert.True(MandatoryClinicCompleteness.AreComplete(
            mandatory,
            new HashSet<Guid> { ClinicEyes, ClinicEnt, ClinicInternal }));
    }

    #region Test helpers

    private static SupervisorAssessmentFileService CreateService(
        AppDbContext db,
        Guid centerId,
        bool allowFinalDecision,
        bool allowAttachments = true)
    {
        var gate = new Mock<IPermissionGate>();
        if (allowFinalDecision)
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Clinical.FinalDecision,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(centerId);
        }
        else
        {
            gate.Setup(g => g.EnsureLocalAsync(
                    MilitaryHealthPermissions.Clinical.FinalDecision,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ForbiddenPermissionException());
        }

        gate.Setup(g => g.Has(MilitaryHealthPermissions.Medical.AttachmentsRead))
            .Returns(allowAttachments);

        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-1", "actor", "127.0.0.1");

        var config = new TypeAdapterConfig();
        new MemberProfile().Register(config);

        var medicalRequests = new MemberMedicalRequestService(
            new ConsultationRepository(db),
            new InvestigationRepository(db),
            new AttachmentRepository(db),
            new MemberRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            gate.Object);

        return new SupervisorAssessmentFileService(
            gate.Object,
            currentUser,
            new MemberRepository(db),
            new ClinicWorkspaceRepository(db),
            new CenterClinicRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            medicalRequests,
            new AuditLogRepository(db),
            new SupervisorTestUnitOfWork(db),
            new Mapper(config));
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, Guid centerId, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب مشرف",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-25),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = recruitment,
            RecruitmentCenterId = RecruitmentCenterId,
            BloodTypeId = 1,
            HeightCm = 170,
            WeightKg = 66,
            BloodPressure = "120/80",
            PulseBpm = 72,
            HasTattoo = false
        });
        return created.MemberId;
    }

    private static async Task<Guid> SeedAssessmentAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        short resultStatusId,
        string? rejectionReason = null)
    {
        var examId = Guid.NewGuid();
        var assessmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ExaminerUserId = "actor-1",
            ExaminedAt = now,
            ExamSequence = 1,
            IsCurrent = true,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = now,
            UpdatedAt = now
        });

        db.ClinicAssessments.Add(new ClinicAssessment
        {
            AssessmentId = assessmentId,
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            ClinicalNotes = "ملاحظات سريرية",
            AssessedByUserId = "actor-1",
            AssessedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
        return assessmentId;
    }

    private static async Task SeedGeneralNoteAsync(
        AppDbContext db,
        Guid memberId,
        Guid clinicId,
        string text)
    {
        var now = DateTimeOffset.UtcNow;
        db.MemberGeneralNotes.Add(new Domain.Entities.Patient.MemberGeneralNote
        {
            NoteId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = CenterA,
            NoteText = text,
            AuthorUserId = "actor-1",
            AuthorClinicId = clinicId,
            WrittenAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedDeferredActionAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        Guid assessmentId)
    {
        db.DeferredActions.Add(new DeferredAction
        {
            DeferredActionId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ClinicAssessmentId = assessmentId,
            DeferReason = "يحتاج متابعة",
            ActionTypeId = 1,
            ActionDescription = "تقرير طبي",
            FollowupNote = "ملاحظة متابعة",
            Status = "REQUESTED",
            RequestedByUserId = "actor-1",
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedConsultationAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        string status,
        string reason,
        string? resultText = null)
    {
        var id = Guid.NewGuid();
        db.Consultations.Add(new Consultation
        {
            ConsultationId = id,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            RequestedByUserId = "actor-1",
            ConsultationTypeId = 1,
            ReferralReason = reason,
            Status = status,
            ResultText = resultText,
            RequestedAt = DateTimeOffset.UtcNow,
            ResultedAt = resultText is null ? null : DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedInvestigationAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        string status,
        string testName,
        string reason)
    {
        db.Investigations.Add(new Investigation
        {
            InvestigationId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            RequestedByUserId = "actor-1",
            InvestigationTypeId = 1,
            TestName = testName,
            RequestReason = reason,
            Status = status,
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedAttachmentAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid? consultationId = null,
        string storageKey = "private/secret/path.pdf")
    {
        var fileId = Guid.NewGuid();
        db.FileAssets.Add(new FileAsset
        {
            FileAssetId = fileId,
            CenterId = centerId,
            StorageKey = storageKey,
            OriginalName = "report.pdf",
            MimeType = "application/pdf",
            Extension = ".pdf",
            SizeBytes = 2048,
            UploadedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false
        });
        db.MedicalAttachments.Add(new MedicalAttachment
        {
            AttachmentId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = centerId,
            FileAssetId = fileId,
            ConsultationId = consultationId,
            UploadedByUserId = "actor-1",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedClinicAsync(db, ClinicEnt, "ENT", "الأذنية", 2);
        await SeedClinicAsync(db, ClinicInternal, "INTERNAL", "الباطنة", 3);
        await SeedClinicAsync(db, ClinicSurgery, "SURGERY", "الجراحة", 4);
        await SeedClinicAsync(db, ClinicOrtho, "ORTHOPEDIC", "العظمية", 5);

        foreach (var clinicId in AllClinics)
        {
            await SeedCenterClinicAsync(db, CenterA, clinicId);
            await SeedCenterClinicAsync(db, CenterB, clinicId);
        }

        if (!await db.ResultStatuses.AnyAsync())
        {
            db.ResultStatuses.AddRange(
                new ResultStatus { StatusId = 1, Code = "ACCEPTED", NameAr = "مقبول", IsActive = true },
                new ResultStatus { StatusId = 2, Code = "REJECTED", NameAr = "مرفوض", IsActive = true },
                new ResultStatus { StatusId = 3, Code = "DEFERRED", NameAr = "مؤجل", IsActive = true });
        }

        if (!await db.RequiredActionTypes.AnyAsync(t => t.ActionTypeId == 1))
        {
            db.RequiredActionTypes.Add(new RequiredActionType
            {
                ActionTypeId = 1,
                Code = "REPORT",
                NameAr = "تقرير طبي",
                RequiresDescription = true,
                RequiresTargetClinic = false,
                IsActive = true
            });
        }

        if (!await db.ConsultationTypes.AnyAsync(t => t.ConsultationTypeId == 1))
        {
            db.ConsultationTypes.Add(new ConsultationType
            {
                ConsultationTypeId = 1,
                Code = "CARDIO",
                NameAr = "قلبية",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        if (!await db.InvestigationTypes.AnyAsync(t => t.InvestigationTypeId == 1))
        {
            db.InvestigationTypes.Add(new InvestigationType
            {
                InvestigationTypeId = 1,
                Code = "LAB",
                NameAr = "مخبري",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedClinicAsync(AppDbContext db, Guid clinicId, string code, string nameAr, int sort)
    {
        if (await db.Clinics.AnyAsync(c => c.ClinicId == clinicId))
            return;

        db.Clinics.Add(new Clinic
        {
            ClinicId = clinicId,
            Code = code,
            NameAr = nameAr,
            SortOrder = (short)sort,
            CountsForFinal = true,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCenterClinicAsync(AppDbContext db, Guid centerId, Guid clinicId)
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

    private sealed class SupervisorTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public SupervisorTestUnitOfWork(AppDbContext db) => _db = db;
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
