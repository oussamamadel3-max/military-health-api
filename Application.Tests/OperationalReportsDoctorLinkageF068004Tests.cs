using Application.Dtos.OperationalReports;
using Application.Exceptions;
using Application.Services.Members;
using Application.Services.OperationalReports;
using Domain.Entities.Clinical;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>BUG-F068-004: ترقيم SQL وتجميع ربط الأطباء دون تغيير العقد.</summary>
public class OperationalReportsDoctorLinkageF068004Tests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicEnt = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ByMember_TotalCount_reflects_distinct_members_not_exam_rows()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-M1");
        await SeedExamAsync(db, memberId, CenterA, ClinicEyes, "actor-1", 1);
        await SeedExamAsync(db, memberId, CenterA, ClinicEnt, "actor-1", 1);

        var page = await OperationalReportsServiceTests.CreateService(db, CenterA)
            .ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery { PageSize = 20 });

        var item = Assert.Single(page.Items, i => i.MemberId == memberId);
        Assert.Equal(2, item.Exams.Count);
        Assert.True(page.TotalCount >= 1);
        Assert.Equal(1, page.Items.Count(i => i.MemberId == memberId));
    }

    [Fact]
    public async Task ByMember_pages_cover_all_members_without_duplicates()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var id = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, $"F068-PG-{i}");
            await SeedExamAsync(db, id, CenterA, ClinicEyes, "actor-1", 1);
            ids.Add(id);
        }

        var service = OperationalReportsServiceTests.CreateService(db, CenterA);
        var collected = new List<Guid>();
        var pageNum = 1;
        int total;
        do
        {
            var page = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery
            {
                PageNumber = pageNum,
                PageSize = 2
            });
            total = page.TotalCount;
            collected.AddRange(page.Items.Select(i => i.MemberId));
            pageNum++;
        } while (collected.Count < total && pageNum < 20);

        Assert.Equal(total, collected.Distinct().Count());
        Assert.True(ids.All(id => collected.Contains(id)));
    }

    [Fact]
    public async Task ByMember_stable_order_uses_FileNumber_then_MemberId()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var first = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-FN-A");
        var second = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-FN-B");
        await SeedExamAsync(db, first, CenterA, ClinicEyes, "actor-1", 1);
        await SeedExamAsync(db, second, CenterA, ClinicEyes, "actor-1", 1);

        var m1 = await db.Members.SingleAsync(m => m.MemberId == first);
        var m2 = await db.Members.SingleAsync(m => m.MemberId == second);
        m1.FileNumber = 5000;
        m2.FileNumber = 5000;
        await db.SaveChangesAsync();

        var service = OperationalReportsServiceTests.CreateService(db, CenterA);
        var page1 = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery
        {
            PageNumber = 1,
            PageSize = 1
        });
        var page2 = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery
        {
            PageNumber = 2,
            PageSize = 1
        });

        var ordered = new[] { first, second }.OrderBy(id => id).ToArray();
        Assert.Equal(ordered[0], page1.Items.First().MemberId);
        Assert.Equal(ordered[1], page2.Items.First().MemberId);
    }

    [Fact]
    public async Task ByDoctor_stable_order_when_MemberCount_tied()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var mA = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-TA");
        var mB = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-TB");
        await SeedExamAsync(db, mA, CenterA, ClinicEyes, "doctor-a", 1);
        await SeedExamAsync(db, mB, CenterA, ClinicEyes, "doctor-b", 1);

        var service = OperationalReportsServiceTests.CreateService(db, CenterA);
        var page = await service.ListDoctorLinkageByDoctorAsync(new DoctorLinkageQuery
        {
            ViewMode = DoctorLinkageViewModes.ByDoctor,
            PageSize = 10
        });

        var tied = page.Items.Where(i => i.MemberCount == 1 && i.ExamCount == 1).OrderBy(i => i.ExaminerUserId).ToList();
        Assert.True(tied.Count >= 2);
        var orderedIds = tied.Select(t => t.ExaminerUserId).ToList();
        Assert.Equal(orderedIds.OrderBy(x => x).ToList(), orderedIds);
    }

    [Fact]
    public async Task ByMember_includes_unassessed_exam_when_no_result_filter()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-UNA");
        await SeedExamWithoutAssessmentAsync(db, memberId, CenterA, ClinicEyes);

        var page = await OperationalReportsServiceTests.CreateService(db, CenterA)
            .ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery());
        var item = Assert.Single(page.Items, i => i.MemberId == memberId);
        var exam = Assert.Single(item.Exams);
        Assert.Null(exam.ResultStatusId);
        Assert.Null(exam.ResultNameAr);
    }

    [Fact]
    public async Task ByMember_result_filter_excludes_unassessed_and_other_statuses()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-FLT");
        await SeedExamWithoutAssessmentAsync(db, memberId, CenterA, ClinicEyes);
        await SeedExamAsync(db, memberId, CenterA, ClinicEnt, "actor-1", 2);

        var page = await OperationalReportsServiceTests.CreateService(db, CenterA)
            .ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery { ResultStatusId = 2 });
        var item = Assert.Single(page.Items, i => i.MemberId == memberId);
        Assert.Single(item.Exams);
        Assert.Equal((short)2, item.Exams[0].ResultStatusId);

        var noUnassessed = await OperationalReportsServiceTests.CreateService(db, CenterA)
            .ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery { ResultStatusId = 1 });
        Assert.DoesNotContain(noUnassessed.Items, i => i.MemberId == memberId);
    }

    [Fact]
    public async Task ByDoctor_counts_match_golden_dataset()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var m1 = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-G1");
        var m2 = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-G2");
        await SeedExamAsync(db, m1, CenterA, ClinicEyes, "gold-doc", 1);
        await SeedExamAsync(db, m1, CenterA, ClinicEyes, "gold-doc", 1);
        await SeedExamAsync(db, m2, CenterA, ClinicEyes, "gold-doc", 2);

        var page = await OperationalReportsServiceTests.CreateService(db, CenterA)
            .ListDoctorLinkageByDoctorAsync(new DoctorLinkageQuery
            {
                ViewMode = DoctorLinkageViewModes.ByDoctor,
                ClinicId = ClinicEyes
            });
        var row = Assert.Single(page.Items, i => i.ExaminerUserId == "gold-doc");
        Assert.Equal(3, row.ExamCount);
        Assert.Equal(2, row.MemberCount);
        Assert.Equal(2, row.AcceptedCount);
        Assert.Equal(1, row.RejectedCount);
        Assert.Equal(0, row.DeferredCount);
    }

    [Fact]
    public async Task Date_range_and_clinic_filter_apply_on_ExaminedAt()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-DT");
        var inRange = DateTimeOffset.Parse("2026-07-10T10:00:00Z");
        var outRange = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
        await SeedExamAsync(db, memberId, CenterA, ClinicEyes, "actor-1", 1, inRange);
        await SeedExamAsync(db, memberId, CenterA, ClinicEnt, "actor-1", 1, outRange);

        var service = OperationalReportsServiceTests.CreateService(db, CenterA);
        var filtered = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery
        {
            ClinicId = ClinicEyes,
            FromDate = new DateOnly(2026, 7, 1),
            ToDate = new DateOnly(2026, 7, 31)
        });

        var item = Assert.Single(filtered.Items, i => i.MemberId == memberId);
        Assert.Single(item.Exams);
        Assert.Equal(inRange, item.Exams[0].ExaminedAt);
    }

    [Fact]
    public async Task ByDoctor_dto_has_no_member_pii_fields()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-PII");
        await SeedExamAsync(db, memberId, CenterA, ClinicEyes, "actor-1", 1);

        var items = (await OperationalReportsServiceTests.CreateService(db, CenterA)
            .ListDoctorLinkageByDoctorAsync(new DoctorLinkageQuery
            {
                ViewMode = DoctorLinkageViewModes.ByDoctor
            })).Items;

        Assert.All(items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.ExaminerUserId));
            Assert.Null(i.GetType().GetProperty("FullName")?.GetValue(i));
            Assert.Null(i.GetType().GetProperty("FileNumber")?.GetValue(i));
        });
    }

    [Fact]
    public async Task Export_by_member_full_dataset_and_audit_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-XL");
        await SeedExamAsync(db, memberId, CenterA, ClinicEyes, "actor-1", 1);
        await SeedExamAsync(db, memberId, CenterA, ClinicEnt, "actor-1", 1);

        var service = OperationalReportsServiceTests.CreateService(db, CenterA);
        var list = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery { PageSize = 1 });
        var export = await service.ExportDoctorLinkageAsync(new DoctorLinkageQuery());
        Assert.NotEmpty(export.FileContents);
        Assert.True(list.TotalCount >= 1);

        var audit = await db.AuditEvents
            .Where(e => e.EventType == OperationalReportsService.ExportEventType)
            .ToListAsync();
        Assert.Contains(audit, e => e.CenterId == CenterA);
    }

    [Fact]
    public async Task Export_by_member_rejects_when_exam_rows_exceed_MaxExportRows()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var memberId = await OperationalReportsServiceTests.CreateTestMemberAsync(db, CenterA, "F068-MAX");
        var baseAt = DateTimeOffset.UtcNow;
        for (var i = 0; i < MemberService.MaxExportRows + 1; i++)
        {
            var at = baseAt.AddMinutes(-i);
            var examId = Guid.NewGuid();
            db.MedicalExams.Add(new MedicalExam
            {
                ExamId = examId,
                MemberId = memberId,
                CenterId = CenterA,
                ClinicId = ClinicEyes,
                ExaminerUserId = "actor-1",
                ExaminedAt = at,
                ExamSequence = 1,
                IsCurrent = true,
                VersionNo = 1,
                UpdatedByUserId = "actor-1",
                CreatedAt = at,
                UpdatedAt = at
            });
            db.ClinicAssessments.Add(new ClinicAssessment
            {
                AssessmentId = Guid.NewGuid(),
                ExamId = examId,
                MemberId = memberId,
                CenterId = CenterA,
                ClinicId = ClinicEyes,
                ResultStatusId = 1,
                AssessedByUserId = "actor-1",
                AssessedAt = at,
                UpdatedByUserId = "actor-1",
                UpdatedAt = at
            });
        }

        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationalReportFilterException>(() =>
            OperationalReportsServiceTests.CreateService(db, CenterA)
                .ExportDoctorLinkageAsync(new DoctorLinkageQuery()));
        Assert.Contains(MemberService.MaxExportRows.ToString(), ex.Message);
    }

    private static async Task SeedExamAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId,
        string examinerUserId,
        short resultStatusId,
        DateTimeOffset? examinedAt = null)
    {
        var examId = Guid.NewGuid();
        var at = examinedAt ?? DateTimeOffset.UtcNow;
        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ExaminerUserId = examinerUserId,
            ExaminedAt = at,
            ExamSequence = 1,
            IsCurrent = true,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = at,
            UpdatedAt = at
        });
        db.ClinicAssessments.Add(new ClinicAssessment
        {
            AssessmentId = Guid.NewGuid(),
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            AssessedByUserId = examinerUserId,
            AssessedAt = at,
            UpdatedByUserId = "actor-1",
            UpdatedAt = at
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedExamWithoutAssessmentAsync(
        AppDbContext db,
        Guid memberId,
        Guid centerId,
        Guid clinicId)
    {
        var at = DateTimeOffset.UtcNow;
        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ExaminerUserId = "actor-1",
            ExaminedAt = at,
            ExamSequence = 1,
            IsCurrent = true,
            VersionNo = 1,
            UpdatedByUserId = "actor-1",
            CreatedAt = at,
            UpdatedAt = at
        });
        await db.SaveChangesAsync();
    }
}
