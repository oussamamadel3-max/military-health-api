using System.Text.Json;
using Application.Common.Permissions;
using Application.Dtos.ClinicWorkspace;
using Application.Dtos.Members;
using Application.Dtos.OperationalReports;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services;
using Application.Services.ClinicWorkspace;
using Application.Services.OperationalReports;
using Application.Services.Sanitization;
using Domain.Entities.Auth;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

/// <summary>
/// BR-069: ثبات هوية المنفّذ، تسجيل المعدّلين في التدقيق، واتساق تقرير ربط الطبيب.
/// </summary>
public class Br069ClinicalIdentityTests
{
    private const string DoctorA = "doctor-a";
    private const string DoctorB = "doctor-b";
    private const string DoctorC = "doctor-c";

    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClinicInternal = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ClinicSurgery = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ClinicOrtho = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public enum Br069ClinicPath
    {
        ClinicAssessment,
        Eye,
        Ent,
        Internal,
        Surgery,
        Orthopedic
    }

    [Theory]
    [InlineData(Br069ClinicPath.ClinicAssessment)]
    [InlineData(Br069ClinicPath.Eye)]
    [InlineData(Br069ClinicPath.Ent)]
    [InlineData(Br069ClinicPath.Internal)]
    [InlineData(Br069ClinicPath.Surgery)]
    [InlineData(Br069ClinicPath.Orthopedic)]
    public async Task Three_doctor_edits_preserve_executor_record_modifier_audits(Br069ClinicPath path)
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedBr069Async(db, ClinicFor(path));
        var clinicId = ClinicFor(path);
        var memberId = await CreateMemberAsync(db, $"BR069-{path}");
        var auditEventType = AuditEventTypeFor(path);
        var auditEntityType = AuditEntityTypeFor(path);

        await RunThreeDoctorExamFlowAsync(db, path, memberId, clinicId);

        var exam = await db.MedicalExams.AsNoTracking()
            .SingleAsync(e => e.MemberId == memberId && e.ClinicId == clinicId && e.IsCurrent);
        var assessment = await db.ClinicAssessments.AsNoTracking()
            .SingleAsync(a => a.ExamId == exam.ExamId);

        Assert.Equal(DoctorA, exam.ExaminerUserId);
        Assert.Equal(DoctorA, assessment.AssessedByUserId);
        Assert.Equal(DoctorC, exam.UpdatedByUserId);
        Assert.Equal(DoctorC, assessment.UpdatedByUserId);
        Assert.Equal(3, exam.VersionNo);

        var modifierAudits = await db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == auditEventType
                        && e.Result == "SUCCESS"
                        && (e.ActorUserId == DoctorB || e.ActorUserId == DoctorC))
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.ActorUserId)
            .ToListAsync();

        Assert.Equal(2, modifierAudits.Count);
        Assert.Equal(DoctorB, modifierAudits[0].ActorUserId);
        Assert.Equal(DoctorC, modifierAudits[1].ActorUserId);
        Assert.True(modifierAudits[0].OccurredAt <= modifierAudits[1].OccurredAt);

        var auditEntityId = AuditEntityIdFor(path, exam.ExamId, assessment.AssessmentId);
        Assert.All(modifierAudits, a =>
        {
            Assert.Equal(auditEntityType, a.EntityType);
            Assert.Equal(auditEntityId, a.EntityId);
            Assert.Equal(CenterA, a.CenterId);
        });
    }

    [Fact]
    public async Task Three_doctor_flow_doctor_linkage_reports_original_examiner()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedBr069Async(db, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "BR069-LINK");
        await RunThreeDoctorExamFlowAsync(db, Br069ClinicPath.ClinicAssessment, memberId, ClinicEyes);

        var exam = await db.MedicalExams.AsNoTracking()
            .SingleAsync(e => e.MemberId == memberId && e.ClinicId == ClinicEyes);

        var reports = CreateReportsService(db);
        var byMember = await reports.ListDoctorLinkageByMemberAsync(
            new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByMember });
        var row = Assert.Single(byMember.Items, i => i.MemberId == memberId);
        var link = Assert.Single(row.Exams.Where(e => e.ClinicId == ClinicEyes));
        Assert.Equal(DoctorA, link.ExaminerUserId);
        Assert.Equal(exam.ExaminedAt, link.ExaminedAt);

        var byDoctor = await reports.ListDoctorLinkageByDoctorAsync(
            new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor });
        Assert.Contains(byDoctor.Items, i => i.ExaminerUserId == DoctorA && i.ClinicId == ClinicEyes);
        Assert.DoesNotContain(byDoctor.Items, i => i.ExaminerUserId == DoctorB && i.ClinicId == ClinicEyes);
        Assert.DoesNotContain(byDoctor.Items, i => i.ExaminerUserId == DoctorC && i.ClinicId == ClinicEyes);
    }

    [Fact]
    public async Task Consultation_update_result_preserves_requester_and_audits_modifier()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedConsultationInvestigationAsync(db);
        await SeedDoctorAssignmentAsync(db, DoctorB, CenterA, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "BR069-CONS");
        var creator = CreateConsultationService(db, DoctorA);
        var created = await creator.CreateAsync(memberId, ValidConsultationCreate());

        var modifier = CreateConsultationService(db, DoctorB);
        var updated = await modifier.UpdateResultAsync(
            created.ConsultationId,
            new UpdateConsultationResultRequest { ResultText = "نتيجة من طبيب آخر" });

        var row = await db.Consultations.AsNoTracking().SingleAsync(c => c.ConsultationId == created.ConsultationId);
        Assert.Equal(DoctorA, row.RequestedByUserId);
        Assert.Equal(ConsultationService.StatusCompleted, updated.Status);
        Assert.NotNull(updated.ResultedAt);

        var audit = Assert.Single(await db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == ConsultationService.ConsultationResultUpdatedEventType)
            .ToListAsync());
        Assert.Equal(DoctorB, audit.ActorUserId);
        Assert.Equal(created.ConsultationId.ToString(), audit.EntityId);
        Assert.False(string.IsNullOrWhiteSpace(audit.NewValues));
        using var json = JsonDocument.Parse(audit.NewValues!);
        Assert.True(json.RootElement.TryGetProperty("UpdatedByUserId", out var modifierProp));
        Assert.Equal(DoctorB, modifierProp.GetString());
    }

    [Fact]
    public async Task Investigation_update_result_preserves_requester_and_audits_modifier()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedConsultationInvestigationAsync(db);
        await SeedDoctorAssignmentAsync(db, DoctorB, CenterA, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "BR069-INV");
        var creator = CreateInvestigationService(db, DoctorA);
        var created = await creator.CreateAsync(memberId, ValidInvestigationCreate());

        var modifier = CreateInvestigationService(db, DoctorB);
        await modifier.UpdateResultAsync(
            created.InvestigationId,
            new UpdateInvestigationResultRequest { ResultText = "نتيجة تحليل" });

        var row = await db.Investigations.AsNoTracking().SingleAsync(i => i.InvestigationId == created.InvestigationId);
        Assert.Equal(DoctorA, row.RequestedByUserId);

        var audit = Assert.Single(await db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == InvestigationService.InvestigationResultUpdatedEventType)
            .ToListAsync());
        Assert.Equal(DoctorB, audit.ActorUserId);
        Assert.Equal(created.InvestigationId.ToString(), audit.EntityId);
        using var json = JsonDocument.Parse(audit.NewValues!);
        Assert.True(json.RootElement.TryGetProperty("UpdatedByUserId", out var modifierProp));
        Assert.Equal(DoctorB, modifierProp.GetString());
    }

    [Fact]
    public async Task Deferred_resubmit_by_other_doctors_preserves_requester_and_audits_each_save()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedBr069Async(db, ClinicEyes);
        var memberId = await CreateMemberAsync(db, "BR069-DEF");
        var deferredRequest = new SaveClinicAssessmentRequest
        {
            ClinicId = ClinicEyes,
            ResultStatusId = 3,
            DeferReason = "سبب تأجيل أ",
            ActionTypeId = 1,
            FollowupNote = "متابعة أ",
            ActionDescription = "وصف أ"
        };

        await AcquireLockAsync(db, memberId, ClinicEyes, DoctorA);
        await CreateClinicAssessmentService(db, DoctorA).SaveAsync(memberId, deferredRequest);

        deferredRequest.DeferReason = "سبب تأجيل ب";
        deferredRequest.FollowupNote = "متابعة ب";
        deferredRequest.ActionDescription = "وصف ب";
        await AcquireLockAsync(db, memberId, ClinicEyes, DoctorB);
        await CreateClinicAssessmentService(db, DoctorB).SaveAsync(memberId, deferredRequest);

        deferredRequest.DeferReason = "سبب تأجيل ج";
        deferredRequest.FollowupNote = "متابعة ج";
        deferredRequest.ActionDescription = "وصف ج";
        await AcquireLockAsync(db, memberId, ClinicEyes, DoctorC);
        await CreateClinicAssessmentService(db, DoctorC).SaveAsync(memberId, deferredRequest);

        var deferred = await db.DeferredActions.AsNoTracking()
            .SingleAsync(d => d.MemberId == memberId && d.ClinicId == ClinicEyes);
        Assert.Equal(DoctorA, deferred.RequestedByUserId);
        Assert.Equal("سبب تأجيل ج", deferred.DeferReason);

        var assessment = await db.ClinicAssessments.AsNoTracking()
            .SingleAsync(a => a.MemberId == memberId && a.ClinicId == ClinicEyes);
        Assert.Equal(DoctorC, assessment.UpdatedByUserId);

        var modifierAudits = await db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == ClinicAssessmentService.AssessmentSavedEventType
                        && e.Result == "SUCCESS"
                        && (e.ActorUserId == DoctorB || e.ActorUserId == DoctorC))
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();
        Assert.Equal(2, modifierAudits.Count);
        Assert.All(modifierAudits, a => Assert.Equal(assessment.AssessmentId.ToString(), a.EntityId));
    }

    #region Three-doctor flow dispatch

    private static async Task RunThreeDoctorExamFlowAsync(
        AppDbContext db,
        Br069ClinicPath path,
        Guid memberId,
        Guid clinicId)
    {
        await AcquireLockAsync(db, memberId, clinicId, DoctorA);
        await SaveAsDoctorAsync(db, path, memberId, DoctorA, accepted: true);

        await AcquireLockAsync(db, memberId, clinicId, DoctorB);
        await SaveAsDoctorAsync(db, path, memberId, DoctorB, accepted: false, rejectionReason: "رفض ب");

        await AcquireLockAsync(db, memberId, clinicId, DoctorC);
        await SaveAsDoctorAsync(db, path, memberId, DoctorC, accepted: false, rejectionReason: "رفض ج");
    }

    private static Task SaveAsDoctorAsync(
        AppDbContext db,
        Br069ClinicPath path,
        Guid memberId,
        string doctorId,
        bool accepted,
        string? rejectionReason = null)
    {
        var resultStatusId = accepted ? (short)1 : (short)2;
        return path switch
        {
            Br069ClinicPath.ClinicAssessment => CreateClinicAssessmentService(db, doctorId).SaveAsync(
                memberId,
                new SaveClinicAssessmentRequest
                {
                    ClinicId = ClinicFor(path),
                    ResultStatusId = resultStatusId,
                    RejectionReason = rejectionReason
                }),
            Br069ClinicPath.Eye => CreateEyeExamService(db, doctorId).SaveAsync(
                memberId,
                EyeRequest(ClinicEyes, resultStatusId, rejectionReason)),
            Br069ClinicPath.Ent => CreateEntExamService(db, doctorId).SaveAsync(
                memberId,
                EntRequest(ClinicEnt, resultStatusId, rejectionReason)),
            Br069ClinicPath.Internal => CreateInternalExamService(db, doctorId).SaveAsync(
                memberId,
                InternalRequest(ClinicInternal, resultStatusId, rejectionReason)),
            Br069ClinicPath.Surgery => CreateSurgeryExamService(db, doctorId).SaveAsync(
                memberId,
                SurgeryRequest(ClinicSurgery, resultStatusId, rejectionReason)),
            Br069ClinicPath.Orthopedic => CreateOrthopedicExamService(db, doctorId).SaveAsync(
                memberId,
                OrthopedicRequest(ClinicOrtho, resultStatusId, rejectionReason)),
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };
    }

    private static Guid ClinicFor(Br069ClinicPath path) => path switch
    {
        Br069ClinicPath.ClinicAssessment => ClinicEyes,
        Br069ClinicPath.Eye => ClinicEyes,
        Br069ClinicPath.Ent => ClinicEnt,
        Br069ClinicPath.Internal => ClinicInternal,
        Br069ClinicPath.Surgery => ClinicSurgery,
        Br069ClinicPath.Orthopedic => ClinicOrtho,
        _ => throw new ArgumentOutOfRangeException(nameof(path))
    };

    private static string AuditEventTypeFor(Br069ClinicPath path) => path switch
    {
        Br069ClinicPath.ClinicAssessment => ClinicAssessmentService.AssessmentSavedEventType,
        Br069ClinicPath.Eye => EyeExamService.EyeExamSavedEventType,
        Br069ClinicPath.Ent => EntExamService.EntExamSavedEventType,
        Br069ClinicPath.Internal => InternalExamService.InternalExamSavedEventType,
        Br069ClinicPath.Surgery => SurgeryExamService.SurgeryExamSavedEventType,
        Br069ClinicPath.Orthopedic => OrthopedicExamService.OrthopedicExamSavedEventType,
        _ => throw new ArgumentOutOfRangeException(nameof(path))
    };

    private static string AuditEntityTypeFor(Br069ClinicPath path) => path switch
    {
        Br069ClinicPath.ClinicAssessment => nameof(ClinicAssessment),
        Br069ClinicPath.Eye => nameof(EyeExamDetail),
        Br069ClinicPath.Ent => nameof(EntExamDetail),
        Br069ClinicPath.Internal => nameof(InternalExamDetail),
        Br069ClinicPath.Surgery => nameof(SurgeryExamDetail),
        Br069ClinicPath.Orthopedic => nameof(OrthopedicExamDetail),
        _ => throw new ArgumentOutOfRangeException(nameof(path))
    };

    private static string AuditEntityIdFor(Br069ClinicPath path, Guid examId, Guid assessmentId) =>
        path == Br069ClinicPath.ClinicAssessment ? assessmentId.ToString() : examId.ToString();

    #endregion

    #region Service factories

    private static ClinicAssessmentService CreateClinicAssessmentService(AppDbContext db, string userId)
        => new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new Br069UnitOfWork(db),
            CreatePermissionGate(CenterA).Object,
            CreateCurrentUser(userId));

    private static EyeExamService CreateEyeExamService(AppDbContext db, string userId)
        => new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new Br069UnitOfWork(db),
            CreatePermissionGate(CenterA).Object,
            CreateCurrentUser(userId));

    private static EntExamService CreateEntExamService(AppDbContext db, string userId)
        => new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new Br069UnitOfWork(db),
            CreatePermissionGate(CenterA).Object,
            CreateCurrentUser(userId));

    private static InternalExamService CreateInternalExamService(AppDbContext db, string userId)
        => new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new Br069UnitOfWork(db),
            CreatePermissionGate(CenterA).Object,
            CreateCurrentUser(userId));

    private static SurgeryExamService CreateSurgeryExamService(AppDbContext db, string userId)
        => new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new Br069UnitOfWork(db),
            CreatePermissionGate(CenterA).Object,
            CreateCurrentUser(userId));

    private static OrthopedicExamService CreateOrthopedicExamService(AppDbContext db, string userId)
        => new(
            new ClinicWorkspaceRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true),
            new AuditLogRepository(db),
            new Br069UnitOfWork(db),
            CreatePermissionGate(CenterA).Object,
            CreateCurrentUser(userId));

    private static ConsultationService CreateConsultationService(AppDbContext db, string userId)
        => new(
            new ConsultationRepository(db),
            new MemberRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            new AuditLogRepository(db),
            new Br069UnitOfWork(db),
            CreateConsultationGate(CenterA).Object,
            CreateCurrentUser(userId),
            new HtmlTextInputSanitizer());

    private static InvestigationService CreateInvestigationService(AppDbContext db, string userId)
        => new(
            new InvestigationRepository(db),
            new ClinicWorkspaceRepository(db),
            new MemberRepository(db),
            new ReferenceDataRepository(db),
            new UserRepository(db),
            new CenterClinicRepository(db),
            new AuditLogRepository(db),
            new Br069UnitOfWork(db),
            CreateConsultationGate(CenterA).Object,
            CreateCurrentUser(userId),
            new HtmlTextInputSanitizer());

    private static OperationalReportsService CreateReportsService(AppDbContext db)
    {
        var gate = CreateReportsGate(CenterA);
        var currentUser = new TestCurrentUser();
        currentUser.SetUser(DoctorA, DoctorA, "127.0.0.1");
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

        return new OperationalReportsService(
            gate.Object,
            currentUser,
            new MemberRepository(db),
            new ClinicWorkspaceRepository(db),
            new CenterClinicRepository(db),
            new ReferenceDataRepository(db),
            new MedicalCenterRepository(db),
            new MemberArchiveRepository(db),
            new UserRepository(db),
            new ExportService(),
            cross.Object,
            new AuditLogRepository(db),
            new Br069UnitOfWork(db));
    }

    private static Mock<IPermissionGate> CreatePermissionGate(Guid centerId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);
        return gate;
    }

    private static Mock<IPermissionGate> CreateConsultationGate(Guid centerId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Medical.ConsultationsInvestigations,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);
        return gate;
    }

    private static Mock<IPermissionGate> CreateReportsGate(Guid centerId)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Reports.Local,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(centerId);
        gate.Setup(g => g.Has(MilitaryHealthPermissions.Members.View)).Returns(true);
        return gate;
    }

    private static TestCurrentUser CreateCurrentUser(string userId)
    {
        var currentUser = new TestCurrentUser();
        currentUser.SetUser(userId, userId, "127.0.0.1");
        return currentUser;
    }

    #endregion

    #region Request builders

    private static SaveEyeExamRequest EyeRequest(Guid clinicId, short resultStatusId, string? rejectionReason)
        => new()
        {
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            VisualAcuityRight = "6/6",
            VisualAcuityLeft = "6/6",
            ColorTestResult = "طبيعي"
        };

    private static SaveEntExamRequest EntRequest(Guid clinicId, short resultStatusId, string? rejectionReason)
        => new()
        {
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            RightEar = "طبيعي",
            LeftEar = "طبيعي",
            TympanicMembrane = "سليم",
            HearingTest = "طبيعي",
            WhisperTest = "مسموع",
            NoseExam = "طبيعي",
            MouthExam = "طبيعي",
            OtherEntDiseases = "لا"
        };

    private static SaveInternalExamRequest InternalRequest(Guid clinicId, short resultStatusId, string? rejectionReason)
        => new()
        {
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            Cardiovascular = "طبيعي",
            Respiratory = "طبيعي",
            Digestive = "طبيعي",
            Endocrine = "طبيعي",
            Neurological = "طبيعي",
            Hematology = "طبيعي",
            Joints = "طبيعي",
            Renal = "طبيعي",
            Skin = "طبيعي"
        };

    private static SaveSurgeryExamRequest SurgeryRequest(Guid clinicId, short resultStatusId, string? rejectionReason)
        => new()
        {
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            GeneralSurgery = "طبيعي",
            Urology = "طبيعي",
            VascularThoracic = "طبيعي"
        };

    private static SaveOrthopedicExamRequest OrthopedicRequest(
        Guid clinicId,
        short resultStatusId,
        string? rejectionReason)
        => new()
        {
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            Musculoskeletal = "طبيعي",
            Neurosurgery = "طبيعي"
        };

    private static CreateConsultationRequest ValidConsultationCreate()
        => new()
        {
            ClinicId = ClinicEyes,
            ConsultationTypeId = 1,
            ReferralReason = "إحالة BR-069"
        };

    private static CreateInvestigationRequest ValidInvestigationCreate()
        => new()
        {
            ClinicId = ClinicEyes,
            InvestigationTypeId = 1,
            TestName = "تحليل عام",
            RequestReason = "سبب"
        };

    #endregion

    #region Seed and fixtures

    private static async Task SeedBr069Async(AppDbContext db, Guid primaryClinicId)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        foreach (var (id, code, name, sort) in new (Guid, string, string, int)[]
                 {
                     (ClinicEyes, "EYE", "العيون", 1),
                     (ClinicEnt, "ENT", "الأذنية", 2),
                     (ClinicInternal, "INTERNAL", "الباطنة", 3),
                     (ClinicSurgery, "SURGERY", "الجراحة", 4),
                     (ClinicOrtho, "ORTHOPEDIC", "العظمية", 5)
                 })
        {
            await SeedClinicAsync(db, id, code, name, sort);
            await SeedCenterClinicAsync(db, CenterA, id);
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
                NameAr = "تقرير",
                RequiresDescription = true,
                RequiresTargetClinic = false,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();

        foreach (var doctor in new[] { DoctorA, DoctorB, DoctorC })
        {
            await SeedUserAsync(db, doctor, doctor);
            await SeedDoctorAssignmentAsync(db, doctor, CenterA, primaryClinicId);
        }
    }

    private static async Task SeedConsultationInvestigationAsync(AppDbContext db)
    {
        await MemberServiceTests.SeedReferencesAsync(db);
        await SeedClinicAsync(db, ClinicEyes, "EYE", "العيون", 1);
        await SeedCenterClinicAsync(db, CenterA, ClinicEyes);

        if (!await db.ConsultationTypes.AnyAsync(t => t.ConsultationTypeId == 1))
        {
            db.ConsultationTypes.Add(new ConsultationType
            {
                ConsultationTypeId = 1,
                Code = "GEN",
                NameAr = "عامة",
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
                NameAr = "مخبر",
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        foreach (var doctor in new[] { DoctorA, DoctorB })
        {
            await SeedUserAsync(db, doctor, doctor);
            await SeedDoctorAssignmentAsync(db, doctor, CenterA, ClinicEyes);
        }
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, CenterA, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = "منتسب BR-069",
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

    private static async Task AcquireLockAsync(AppDbContext db, Guid memberId, Guid clinicId, string userId)
    {
        var existing = await db.ClinicExamEditLocks
            .FirstOrDefaultAsync(l => l.MemberId == memberId && l.ClinicId == clinicId);
        if (existing is not null)
            db.ClinicExamEditLocks.Remove(existing);

        var now = DateTimeOffset.UtcNow;
        db.ClinicExamEditLocks.Add(new ClinicExamEditLock
        {
            LockId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = CenterA,
            ClinicId = clinicId,
            LockedByUserId = userId,
            LockedAt = now,
            ExpiresAt = now.AddMinutes(30)
        });
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

    private static async Task SeedDoctorAssignmentAsync(AppDbContext db, string userId, Guid centerId, Guid clinicId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId))
            await SeedUserAsync(db, userId, userId);

        if (await db.UserClinicAssignments.AnyAsync(a =>
                a.UserId == userId && a.CenterId == centerId && a.ClinicId == clinicId && a.IsActive))
            return;

        db.UserClinicAssignments.Add(new UserClinicAssignment
        {
            AssignmentId = Guid.NewGuid(),
            UserId = userId,
            CenterId = centerId,
            ClinicId = clinicId,
            IsActive = true,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedByUserId = DoctorA
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedUserAsync(AppDbContext db, string userId, string userName)
    {
        if (await db.Users.AnyAsync(u => u.Id == userId))
            return;

        db.Users.Add(new User
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@test.local",
            NormalizedEmail = $"{userName.ToUpperInvariant()}@TEST.LOCAL",
            SecurityStamp = Guid.NewGuid().ToString(),
            IsActive = true,
            FullName = userName
        });
        await db.SaveChangesAsync();
    }

    private sealed class Br069UnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public Br069UnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new Br069NoopTransaction());
    }

    private sealed class Br069NoopTransaction : IDbContextTransaction
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
