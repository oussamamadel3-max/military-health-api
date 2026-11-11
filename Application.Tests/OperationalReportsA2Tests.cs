using Application.Common.Permissions;
using Application.Dtos.OperationalReports;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.ClinicWorkspace;
using Application.Services.OperationalReports;
using Application.Services.Supervisor;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Application.Tests;

/// <summary>
/// FR-062…068 استثناء A2: Reports.Local للمؤشرات؛ + Members.View للتفاصيل المحلية.
/// </summary>
public class OperationalReportsA2Tests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task DoctorLinkage_indicators_only_allows_by_doctor_blocks_by_member()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        await SeedDoctorLinkageAsync(db);

        var indicatorsOnly = CreateReportsService(db, CenterA, allowMemberDetailView: false);

        var byDoctor = await indicatorsOnly.ListDoctorLinkageByDoctorAsync(
            new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByDoctor });
        Assert.NotEmpty(byDoctor.Items);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            indicatorsOnly.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery()));
    }

    [Fact]
    public async Task DoctorLinkage_export_blocks_by_member_allows_by_doctor_for_indicators_only()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        await SeedDoctorLinkageAsync(db);

        var service = CreateReportsService(db, CenterA, allowMemberDetailView: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ExportDoctorLinkageAsync(new DoctorLinkageQuery()));

        var file = await service.ExportDoctorLinkageAsync(new DoctorLinkageQuery
        {
            ViewMode = DoctorLinkageViewModes.ByDoctor
        });
        Assert.NotEmpty(file.FileContents);
    }

    [Fact]
    public async Task DoctorLinkage_full_access_with_reports_and_members_view()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        await SeedDoctorLinkageAsync(db);

        var service = CreateReportsService(db, CenterA, allowMemberDetailView: true);
        var byMember = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery());
        Assert.NotEmpty(byMember.Items);

        var export = await service.ExportDoctorLinkageAsync(new DoctorLinkageQuery());
        Assert.NotEmpty(export.FileContents);
    }

    [Fact]
    public async Task Members_view_alone_does_not_grant_reports_without_local_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        var service = CreateReportsService(db, CenterA, allowLocal: false, allowMemberDetailView: true);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetLocalKpisAsync(new LocalKpiQuery()));
    }

    [Fact]
    public async Task Final_and_waiting_lists_require_member_detail_view()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        var service = CreateReportsService(db, CenterA, allowMemberDetailView: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ListFinalMembersAsync(new FinalMembersQuery()));
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ListWaitingMembersAsync(new WaitingMembersQuery()));
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ExportFinalMembersAsync(new FinalMembersQuery()));
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ExportWaitingMembersAsync(new WaitingMembersQuery()));
    }

    [Fact]
    public async Task Local_kpis_available_with_reports_local_only()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        var service = CreateReportsService(db, CenterA, allowMemberDetailView: false);

        var kpi = await service.GetLocalKpisAsync(new LocalKpiQuery());
        Assert.Equal(CenterA, kpi.CenterId);
    }

    [Fact]
    public async Task Deferred_local_indicators_only_returns_breakdown_without_member_rows()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        var memberId = await OperationalReportsServiceTestsExtensions.CreateMemberAsync(db, CenterA, "DEF-A2");
        db.DeferredActions.Add(new DeferredAction
        {
            DeferredActionId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            DeferReason = "سبب",
            ActionTypeId = 1,
            ActionDescription = "وصف",
            FollowupNote = "متابعة",
            Status = OperationalReportsService.DeferredStatusRequested,
            RequestedByUserId = "actor-1",
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateReportsService(db, CenterA, allowMemberDetailView: false);
        var report = await service.GetDeferredReportAsync(new DeferredReportQuery());
        Assert.Equal(1, report.TotalCount);
        Assert.NotEmpty(report.Breakdown);
        Assert.Empty(report.Items);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ExportDeferredReportAsync(new DeferredReportQuery()));
    }

    [Fact]
    public async Task Deferred_local_with_member_view_returns_detail_rows()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        var memberId = await OperationalReportsServiceTestsExtensions.CreateMemberAsync(db, CenterA, "DEF-OK");
        db.DeferredActions.Add(new DeferredAction
        {
            DeferredActionId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            DeferReason = "سبب",
            ActionTypeId = 1,
            ActionDescription = "وصف",
            FollowupNote = "متابعة",
            Status = OperationalReportsService.DeferredStatusRequested,
            RequestedByUserId = "actor-1",
            RequestedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateReportsService(db, CenterA, allowMemberDetailView: true);
        var report = await service.GetDeferredReportAsync(new DeferredReportQuery());
        Assert.Single(report.Items);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(report.Items).FullName));
    }

    private static async Task SeedDoctorLinkageAsync(AppDbContext db)
    {
        var memberId = await OperationalReportsServiceTestsExtensions.CreateMemberAsync(db, CenterA, "LNK-A2");
        var examId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.MedicalExams.Add(new MedicalExam
        {
            ExamId = examId,
            MemberId = memberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
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
            AssessmentId = Guid.NewGuid(),
            ExamId = examId,
            MemberId = memberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            ResultStatusId = 1,
            AssessedByUserId = "actor-1",
            AssessedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static OperationalReportsService CreateReportsService(
        AppDbContext db,
        Guid centerId,
        bool allowLocal = true,
        bool allowMemberDetailView = true)
        => OperationalReportsServiceTests.CreateService(
            db,
            centerId,
            allowLocal: allowLocal,
            allowMemberDetailView: allowMemberDetailView);
}

internal static class OperationalReportsServiceTestsExtensions
{
    public static Task<Guid> CreateMemberAsync(AppDbContext db, Guid centerId, string recruitment)
        => OperationalReportsServiceTests.CreateTestMemberAsync(db, centerId, recruitment);
}
