using Application.Dtos.OperationalReports;
using Application.Exceptions;
using Application.Services.OperationalReports;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>FR-068 A3 / BUG-F068-005: نشاط الطبيب حسب مركز إجراء الفحص بغض النظر عن نقل المنتسب.</summary>
public class OperationalReportsDoctorLinkageA3Tests
{
    private const string DoctorAhmed = "doctor-ahmed";
    private const string DoctorKhalid = "doctor-khalid";
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ByDoctor_keeps_source_center_activity_after_member_transfer_to_B()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var member = await MemberTransferServiceTests.CreateMemberAsync(db, CenterA, 1001, "A3-MOVE");
        await SeedExamWithAssessmentAsync(db, member.MemberId, CenterA, ClinicEyes, DoctorAhmed, accepted: true);

        var reportsA = OperationalReportsServiceTests.CreateService(db, CenterA);
        var before = Assert.Single(
            (await reportsA.ListDoctorLinkageByDoctorAsync(
                new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor })).Items,
            i => i.ExaminerUserId == DoctorAhmed && i.ClinicId == ClinicEyes);

        Assert.Equal(1, before.ExamCount);
        Assert.Equal(1, before.MemberCount);

        await MemberTransferServiceTests.CreateService(db, CenterA, allowTransfer: true)
            .TransferLocalAsync(member.MemberId, new Application.Dtos.Members.TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "نقل لاختبار A3"
            });

        var after = Assert.Single(
            (await reportsA.ListDoctorLinkageByDoctorAsync(
                new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor })).Items,
            i => i.ExaminerUserId == DoctorAhmed && i.ClinicId == ClinicEyes);

        Assert.Equal(before.ExamCount, after.ExamCount);
        Assert.Equal(before.MemberCount, after.MemberCount);
        Assert.Equal(before.AcceptedCount, after.AcceptedCount);

        var reportsB = OperationalReportsServiceTests.CreateService(db, CenterB);
        var atB = await reportsB.ListDoctorLinkageByDoctorAsync(
            new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor });
        Assert.DoesNotContain(atB.Items, i => i.ExaminerUserId == DoctorAhmed && i.ExamCount > 0);
    }

    [Fact]
    public async Task ByMember_hides_transferred_member_from_source_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var member = await MemberTransferServiceTests.CreateMemberAsync(db, CenterA, 1002, "A3-HIDE");
        await SeedExamWithAssessmentAsync(db, member.MemberId, CenterA, ClinicEyes, DoctorAhmed, accepted: true);

        await MemberTransferServiceTests.CreateService(db, CenterA, allowTransfer: true)
            .TransferLocalAsync(member.MemberId, new Application.Dtos.Members.TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "عزل المصدر"
            });

        var byMemberA = await OperationalReportsServiceTests.CreateService(db, CenterA)
            .ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery());
        Assert.DoesNotContain(byMemberA.Items, i => i.MemberId == member.MemberId);
    }

    [Fact]
    public async Task ByMember_at_destination_unchanged_without_exam_at_that_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var member = await MemberTransferServiceTests.CreateMemberAsync(db, CenterA, 1003, "A3-BYMB");
        await SeedExamWithAssessmentAsync(db, member.MemberId, CenterA, ClinicEyes, DoctorAhmed, accepted: true);

        await MemberTransferServiceTests.CreateService(db, CenterA, allowTransfer: true)
            .TransferLocalAsync(member.MemberId, new Application.Dtos.Members.TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "نقل"
            });

        var byMemberB = await OperationalReportsServiceTests.CreateService(db, CenterB)
            .ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery());
        Assert.DoesNotContain(byMemberB.Items, i => i.MemberId == member.MemberId);
    }

    [Fact]
    public async Task ByDoctor_scopes_exams_per_center_for_multi_center_doctor()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var memberA = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "A3-MA");
        var memberB = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterB, "A3-MB");
        await SeedExamWithAssessmentAsync(db, memberA, CenterA, ClinicEyes, DoctorAhmed, accepted: true);
        await SeedExamWithAssessmentAsync(db, memberB, CenterB, ClinicEnt, DoctorAhmed, accepted: true);

        var atA = Assert.Single(
            (await OperationalReportsServiceTests.CreateService(db, CenterA).ListDoctorLinkageByDoctorAsync(
                new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor })).Items,
            i => i.ExaminerUserId == DoctorAhmed);
        Assert.Equal(ClinicEyes, atA.ClinicId);
        Assert.Equal(1, atA.ExamCount);

        var atB = Assert.Single(
            (await OperationalReportsServiceTests.CreateService(db, CenterB).ListDoctorLinkageByDoctorAsync(
                new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor })).Items,
            i => i.ExaminerUserId == DoctorAhmed);
        Assert.Equal(ClinicEnt, atB.ClinicId);
        Assert.Equal(1, atB.ExamCount);
    }

    [Fact]
    public async Task ByDoctor_ignores_non_current_exams()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "A3-CUR");
        await SeedExamWithAssessmentAsync(db, memberId, CenterA, ClinicEyes, DoctorAhmed, accepted: true, isCurrent: false);

        var page = await OperationalReportsServiceTests.CreateService(db, CenterA).ListDoctorLinkageByDoctorAsync(
            new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor });
        Assert.DoesNotContain(page.Items, i => i.ExaminerUserId == DoctorAhmed && i.ExamCount > 0);
    }

    [Fact]
    public async Task A2_by_doctor_allowed_without_member_view()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "A3-A2");
        await SeedExamWithAssessmentAsync(db, memberId, CenterA, ClinicEyes, DoctorAhmed, accepted: true);

        var service = OperationalReportsServiceTests.CreateService(db, CenterA, allowMemberDetailView: false);
        var page = await service.ListDoctorLinkageByDoctorAsync(
            new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor });
        Assert.NotEmpty(page.Items);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery()));
    }

    /// <summary>قرار BY_MEMBER (A): منتسب حالي + فحوص مركز التقرير فقط؛ BY_DOCTOR عند مركز إجراء الفحص.</summary>
    [Fact]
    public async Task Full_transfer_A_to_B_by_member_center_scoped_exams_and_exports()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var member = await MemberTransferServiceTests.CreateMemberAsync(db, CenterA, 1099, "FR068-MOH");
        await SeedExamWithAssessmentAsync(db, member.MemberId, CenterA, ClinicEyes, DoctorAhmed, accepted: true);

        await MemberTransferServiceTests.CreateService(db, CenterA, allowTransfer: true)
            .TransferLocalAsync(member.MemberId, new Application.Dtos.Members.TransferMemberRequest
            {
                ToCenterId = CenterB,
                TransferReason = "نقل محمد لمركز ب"
            });

        await SeedExamWithAssessmentAsync(db, member.MemberId, CenterB, ClinicEnt, DoctorKhalid, accepted: true);

        var reportsA = OperationalReportsServiceTests.CreateService(db, CenterA);
        var reportsB = OperationalReportsServiceTests.CreateService(db, CenterB);

        var byMemberB = await reportsB.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery());
        var row = Assert.Single(byMemberB.Items, i => i.MemberId == member.MemberId);
        var exam = Assert.Single(row.Exams);
        Assert.Equal(DoctorKhalid, exam.ExaminerUserId);
        Assert.Equal(ClinicEnt, exam.ClinicId);
        Assert.DoesNotContain(row.Exams, e => e.ExaminerUserId == DoctorAhmed);

        var byMemberA = await reportsA.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery());
        Assert.DoesNotContain(byMemberA.Items, i => i.MemberId == member.MemberId);

        var byDoctorA = Assert.Single(
            (await reportsA.ListDoctorLinkageByDoctorAsync(new DoctorLinkageQuery
            {
                ViewMode = DoctorLinkageViewModes.ByDoctor
            })).Items,
            i => i.ExaminerUserId == DoctorAhmed && i.ClinicId == ClinicEyes);
        Assert.Equal(1, byDoctorA.ExamCount);
        Assert.Equal(1, byDoctorA.MemberCount);

        var byDoctorB = Assert.Single(
            (await reportsB.ListDoctorLinkageByDoctorAsync(new DoctorLinkageQuery
            {
                ViewMode = DoctorLinkageViewModes.ByDoctor
            })).Items,
            i => i.ExaminerUserId == DoctorKhalid);
        Assert.Equal(1, byDoctorB.ExamCount);
        Assert.DoesNotContain(
            (await reportsB.ListDoctorLinkageByDoctorAsync(new DoctorLinkageQuery
            {
                ViewMode = DoctorLinkageViewModes.ByDoctor
            })).Items,
            i => i.ExaminerUserId == DoctorAhmed && i.ExamCount > 0);

        var exportMemberB = await reportsB.ExportDoctorLinkageAsync(new DoctorLinkageQuery());
        Assert.NotEmpty(exportMemberB.FileContents);

        var exportDoctorA = await reportsA.ExportDoctorLinkageAsync(new DoctorLinkageQuery
        {
            ViewMode = DoctorLinkageViewModes.ByDoctor
        });
        Assert.NotEmpty(exportDoctorA.FileContents);
    }

    [Fact]
    public async Task Export_by_doctor_matches_list_aggregates()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var member = await MemberTransferServiceTests.CreateMemberAsync(db, CenterA, 1004, "A3-XLS");
        await SeedExamWithAssessmentAsync(db, member.MemberId, CenterA, ClinicEyes, DoctorAhmed, accepted: true);

        var service = OperationalReportsServiceTests.CreateService(db, CenterA);
        var list = await service.ListDoctorLinkageByDoctorAsync(
            new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor });
        var export = await service.ExportDoctorLinkageAsync(new DoctorLinkageQuery
        {
            ViewMode = DoctorLinkageViewModes.ByDoctor
        });

        Assert.NotEmpty(export.FileContents);
        Assert.Equal(list.Items.Count, list.TotalCount);

        var audit = Assert.Single(await db.AuditEvents
            .Where(e => e.EventType == OperationalReportsService.ExportEventType)
            .ToListAsync());
        Assert.Equal(CenterA, audit.CenterId);
    }

    private static async Task SeedExamWithAssessmentAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        string examinerUserId,
        bool accepted = true,
        bool isCurrent = true)
    {
        if (!await db.Users.AnyAsync(u => u.Id == examinerUserId))
        {
            db.Users.Add(new Domain.Entities.Auth.User
            {
                Id = examinerUserId,
                UserName = examinerUserId,
                NormalizedUserName = examinerUserId.ToUpperInvariant(),
                Email = $"{examinerUserId}@test.local",
                NormalizedEmail = $"{examinerUserId.ToUpperInvariant()}@TEST.LOCAL",
                SecurityStamp = Guid.NewGuid().ToString(),
                IsActive = true
            });
        }

        short status = accepted ? (short)1 : (short)3;
        var examId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ExaminerUserId = examinerUserId,
            ExaminedAt = now,
            ExamSequence = 1,
            IsCurrent = isCurrent,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ClinicAssessments.Add(new ClinicAssessment
        {
            AssessmentId = Guid.NewGuid(),
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ResultStatusId = status,
            AssessedByUserId = examinerUserId,
            AssessedAt = now,
            UpdatedByUserId = examinerUserId,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
    }
}
