using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Dtos.OperationalReports;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services;
using Application.Services.OperationalReports;
using Application.Services.Supervisor;
using Domain.Entities.Archive;
using Domain.Entities.Clinical;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class OperationalReportsServiceTests
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
    public async Task LocalKpis_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, allowLocal: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetLocalKpisAsync(new LocalKpiQuery()));
    }

    [Fact]
    public async Task LocalKpis_counts_results_and_waiting_within_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        await CreateMemberWithDecisionAsync(db, CenterA, "KPI-ACC", 1);
        await CreateMemberWithDecisionAsync(db, CenterA, "KPI-REJ", 2, "رفض");
        await CreateMemberWithDecisionAsync(db, CenterA, "KPI-DEF", 3, "مؤجل");
        await CreateMemberAsync(db, CenterA, "KPI-WAIT");
        await CreateMemberWithDecisionAsync(db, CenterB, "KPI-B", 1);

        var kpi = await CreateService(db, CenterA).GetLocalKpisAsync(new LocalKpiQuery());

        Assert.Equal(4, kpi.TotalMembers);
        Assert.Equal(1, kpi.AcceptedCount);
        Assert.Equal(1, kpi.RejectedCount);
        Assert.Equal(1, kpi.DeferredCount);
        Assert.Equal(1, kpi.WaitingCount);
        Assert.Equal(0, kpi.ArchivedCount);
        Assert.Equal(CenterA, kpi.CenterId);
    }

    [Fact]
    public async Task LocalKpis_counts_archived_within_filtered_center_members()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        var archivedA = await CreateMemberWithDecisionAsync(db, CenterA, "KPI-ARC-A", 1);
        await CreateMemberWithDecisionAsync(db, CenterA, "KPI-NO-ARC", 1);
        await CreateMemberAsync(db, CenterA, "KPI-WAIT-NO-ARC");
        var archivedB = await CreateMemberWithDecisionAsync(db, CenterB, "KPI-ARC-B", 1);

        await ArchiveMemberAsync(db, archivedA, CenterA);
        await ArchiveMemberAsync(db, archivedB, CenterB);

        var kpi = await CreateService(db, CenterA).GetLocalKpisAsync(new LocalKpiQuery());

        Assert.Equal(3, kpi.TotalMembers);
        Assert.Equal(1, kpi.ArchivedCount);
    }

    [Fact]
    public async Task LocalKpis_isolates_centers()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberWithDecisionAsync(db, CenterA, "ISO-A", 1);
        await CreateMemberWithDecisionAsync(db, CenterB, "ISO-B", 1);

        var a = await CreateService(db, CenterA).GetLocalKpisAsync(new LocalKpiQuery());
        var b = await CreateService(db, CenterB).GetLocalKpisAsync(new LocalKpiQuery());

        Assert.Equal(1, a.TotalMembers);
        Assert.Equal(1, b.TotalMembers);
        Assert.Equal(0, a.ArchivedCount);
        Assert.Equal(0, b.ArchivedCount);
        Assert.Equal(CenterA, a.CenterId);
        Assert.Equal(CenterB, b.CenterId);
    }

    [Fact]
    public async Task CentralKpis_requires_central_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, allowLocal: true, allowCentral: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetCentralKpisAsync(new CentralKpiQuery()));
    }

    [Fact]
    public async Task CentralKpis_aggregates_without_double_count()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberWithDecisionAsync(db, CenterA, "CEN-A1", 1);
        await CreateMemberWithDecisionAsync(db, CenterA, "CEN-A2", 2, "رفض");
        await CreateMemberWithDecisionAsync(db, CenterB, "CEN-B1", 1);

        var result = await CreateService(db, CenterA, allowCentral: true)
            .GetCentralKpisAsync(new CentralKpiQuery());

        Assert.Equal(2, result.Centers.Count);
        Assert.Equal(3, result.Totals.TotalMembers);
        Assert.Equal(2, result.Totals.AcceptedCount);
        Assert.Equal(1, result.Totals.RejectedCount);
        Assert.Contains(OperationalReportsService.AntiDoubleCountMethodology, result.AntiDoubleCountMethodology);
        Assert.Contains("CurrentMedicalCenterId", result.AntiDoubleCountMethodology);
    }

    [Fact]
    public async Task DeferredReport_computes_late_without_persisting_status()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "DEF-LATE");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.DeferredActions.Add(new DeferredAction
        {
            DeferredActionId = Guid.NewGuid(),
            MemberId = memberId,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            DeferReason = "سبب",
            DeferUntil = today.AddDays(-2),
            ActionTypeId = 1,
            FollowupNote = "ملاحظة",
            Status = OperationalReportsService.DeferredStatusRequested,
            RequestedByUserId = "actor-1",
            RequestedAt = DateTimeOffset.UtcNow.AddDays(-5)
        });
        await db.SaveChangesAsync();

        var report = await CreateService(db, CenterA)
            .GetDeferredReportAsync(new DeferredReportQuery { LateOnly = true });

        Assert.Equal(1, report.TotalCount);
        Assert.Equal(1, report.LateCount);
        Assert.True(Assert.Single(report.Items).IsLate);
        Assert.DoesNotContain(await db.DeferredActions.ToListAsync(),
            d => d.Status.Contains("LATE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FinalMembers_filters_by_result_and_paginates()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberWithDecisionAsync(db, CenterA, "FM-1", 1);
        await CreateMemberWithDecisionAsync(db, CenterA, "FM-2", 2, "رفض");
        await CreateMemberWithDecisionAsync(db, CenterA, "FM-3", 1);

        var page = await CreateService(db, CenterA).ListFinalMembersAsync(new FinalMembersQuery
        {
            ResultStatusId = 1,
            PageNumber = 1,
            PageSize = 10
        });

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, i => Assert.Equal((short)1, i.FinalResultStatusId));
    }

    [Fact]
    public async Task LocalSummary_returns_percentages()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberWithDecisionAsync(db, CenterA, "SUM-1", 1);
        await CreateMemberWithDecisionAsync(db, CenterA, "SUM-2", 1);
        await CreateMemberWithDecisionAsync(db, CenterA, "SUM-3", 2, "رفض");
        await CreateMemberAsync(db, CenterA, "SUM-4");

        var summary = await CreateService(db, CenterA).GetLocalSummaryAsync(new LocalSummaryQuery());

        Assert.Equal(4, summary.TotalMembers);
        Assert.Equal(2, summary.AcceptedCount);
        Assert.Equal(50m, summary.AcceptedPercent);
        Assert.Equal(25m, summary.RejectedPercent);
        Assert.Equal(25m, summary.WaitingPercent);
    }

    [Fact]
    public async Task CentralCompare_denied_for_local_only_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            CreateService(db, CenterA, allowLocal: true, allowCentral: false)
                .GetCentralCompareAsync(new CentralCompareQuery()));
    }

    [Fact]
    public async Task DoctorLinkage_by_member_lists_examiner()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberAsync(db, CenterA, "DOC-1");
        await SeedAssessmentAsync(db, memberId, CenterA, ClinicEyes, 1);

        var page = await CreateService(db, CenterA).ListDoctorLinkageByMemberAsync(
            new DoctorLinkageQuery { ViewMode = DoctorLinkageViewModes.ByMember });

        var item = Assert.Single(page.Items);
        Assert.Equal(memberId, item.MemberId);
        Assert.Contains(item.Exams, e => e.ClinicId == ClinicEyes && e.ExaminerUserId == "actor-1");
    }

    [Fact]
    public async Task Invalid_date_range_rejected()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await Assert.ThrowsAsync<InvalidOperationalReportFilterException>(() =>
            CreateService(db, CenterA).GetLocalKpisAsync(new LocalKpiQuery
            {
                FromDate = new DateOnly(2026, 5, 1),
                ToDate = new DateOnly(2026, 1, 1)
            }));
    }

    [Fact]
    public async Task LocalKpis_filters_by_period_result_export_and_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        var oldAccepted = await CreateMemberWithDecisionAsync(db, CenterA, "FLT-OLD", 1);
        var old = await db.Members.SingleAsync(m => m.MemberId == oldAccepted);
        old.CreatedAt = DateTimeOffset.Parse("2025-01-10T00:00:00Z");
        var oldDecision = await db.FinalDecisions.SingleAsync(d => d.MemberId == oldAccepted);
        oldDecision.DecidedAt = DateTimeOffset.Parse("2025-01-10T12:00:00Z");
        await db.SaveChangesAsync();

        var accepted = await CreateMemberWithDecisionAsync(db, CenterA, "FLT-ACC", 1);
        var rejected = await CreateMemberWithDecisionAsync(db, CenterA, "FLT-REJ", 2, "رفض");
        var exported = await CreateMemberWithDecisionAsync(db, CenterA, "FLT-EXP", 1);
        var exportedMember = await db.Members.SingleAsync(m => m.MemberId == exported);
        exportedMember.ExportStatus = FinalDecisionService.ExportStatusExported;
        await db.SaveChangesAsync();

        // منتسب بتقييم عيادة واحدة فقط (غير مكتمل) في العيون.
        var eyeOnly = await CreateMemberAsync(db, CenterA, "FLT-EYE");
        await SeedAssessmentAsync(db, eyeOnly, CenterA, ClinicEyes, 1);

        var service = CreateService(db, CenterA);

        var jan2025 = await service.GetLocalKpisAsync(new LocalKpiQuery
        {
            FromDate = new DateOnly(2025, 1, 1),
            ToDate = new DateOnly(2025, 1, 31)
        });
        Assert.Equal(1, jan2025.TotalMembers);
        Assert.Equal(1, jan2025.AcceptedCount);

        var byResult = await service.GetLocalKpisAsync(new LocalKpiQuery { ResultStatusId = 2 });
        Assert.Equal(1, byResult.TotalMembers);
        Assert.Equal(1, byResult.RejectedCount);
        Assert.Equal(0, byResult.AcceptedCount);

        var byExport = await service.GetLocalKpisAsync(new LocalKpiQuery
        {
            ExportStatus = FinalDecisionService.ExportStatusExported
        });
        Assert.Equal(1, byExport.TotalMembers);
        Assert.Equal(1, byExport.ExportedCount);

        var byClinic = await service.GetLocalKpisAsync(new LocalKpiQuery { ClinicId = ClinicEyes });
        // المكتملون + تقييم العيون فقط يدخلون عند فلتر العيادة.
        Assert.True(byClinic.TotalMembers >= 2);
        Assert.Contains(eyeOnly, new[] { eyeOnly, accepted, rejected, exported, oldAccepted });

        var completeness = await service.GetLocalKpisAsync(new LocalKpiQuery());
        Assert.True(completeness.CompleteExamsCount >= 1);
        Assert.True(completeness.IncompleteExamsCount >= 1);
    }

    [Fact]
    public async Task Local_rejects_other_center_client_binding()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var gate = new Mock<IPermissionGate>(MockBehavior.Strict);
        gate.Setup(g => g.EnsureLocalAsync(
                MilitaryHealthPermissions.Reports.Local,
                CenterB,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenPermissionException());

        var service = CreateService(db, CenterA, configureGate: false, gate: gate);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetLocalKpisAsync(new LocalKpiQuery(), clientCenterId: CenterB));
    }

    [Fact]
    public async Task CentralCompare_sums_two_centers_and_records_cross_access()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberWithDecisionAsync(db, CenterA, "CMP-A1", 1);
        await CreateMemberWithDecisionAsync(db, CenterA, "CMP-A2", 2, "رفض");
        await CreateMemberWithDecisionAsync(db, CenterB, "CMP-B1", 1);

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

        var result = await CreateService(db, CenterA, allowCentral: true, cross: cross)
            .GetCentralCompareAsync(new CentralCompareQuery
            {
                CenterIds = [CenterA, CenterB]
            });

        Assert.Equal(2, result.Centers.Count);
        Assert.Equal(3, result.Totals.TotalMembers);
        Assert.Equal(2, result.Totals.AcceptedCount);
        Assert.Equal(1, result.Totals.RejectedCount);
        Assert.Equal(
            3,
            result.Centers.Single(c => c.CenterId == CenterA).TotalMembers
            + result.Centers.Single(c => c.CenterId == CenterB).TotalMembers);

        cross.Verify(c => c.RecordAsync(
            CenterA,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        cross.Verify(c => c.RecordAsync(
            CenterB,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CentralKpis_records_cross_center_access()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberWithDecisionAsync(db, CenterA, "XA-1", 1);
        await CreateMemberWithDecisionAsync(db, CenterB, "XB-1", 1);

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

        await CreateService(db, CenterA, allowCentral: true, cross: cross)
            .GetCentralKpisAsync(new CentralKpiQuery { CenterIds = [CenterA, CenterB] });

        cross.Verify(c => c.RecordAsync(
            It.IsAny<Guid>(),
            It.Is<string>(r => r.Contains("مؤشرات", StringComparison.Ordinal)),
            "OperationalKpi",
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Deferred_and_doctor_linkage_support_pagination()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        for (var i = 0; i < 3; i++)
        {
            var id = await CreateMemberAsync(db, CenterA, $"PG-D-{i}");
            db.DeferredActions.Add(new DeferredAction
            {
                DeferredActionId = Guid.NewGuid(),
                MemberId = id,
                CenterId = CenterA,
                ClinicId = ClinicEyes,
                DeferReason = "سبب",
                DeferUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3),
                ActionTypeId = 1,
                FollowupNote = "ن",
                Status = OperationalReportsService.DeferredStatusRequested,
                RequestedByUserId = "actor-1",
                RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
            });
            await SeedAssessmentAsync(db, id, CenterA, ClinicEyes, 1);
        }

        await db.SaveChangesAsync();
        var service = CreateService(db, CenterA);

        var deferred = await service.GetDeferredReportAsync(new DeferredReportQuery
        {
            PageNumber = 2,
            PageSize = 1
        });
        Assert.Equal(3, deferred.TotalCount);
        Assert.Equal(2, deferred.PageNumber);
        Assert.Single(deferred.Items);

        var doctors = await service.ListDoctorLinkageByMemberAsync(new DoctorLinkageQuery
        {
            PageNumber = 1,
            PageSize = 2
        });
        Assert.True(doctors.TotalCount >= 3);
        Assert.Equal(2, doctors.Items.Count);
    }

    [Fact]
    public async Task Excel_exports_for_required_reports_succeed()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        await CreateMemberWithDecisionAsync(db, CenterA, "XL-A", 1);
        await CreateMemberWithDecisionAsync(db, CenterB, "XL-B", 2, "رفض");
        var deferredMember = await CreateMemberAsync(db, CenterA, "XL-DEF");
        db.DeferredActions.Add(new DeferredAction
        {
            DeferredActionId = Guid.NewGuid(),
            MemberId = deferredMember,
            CenterId = CenterA,
            ClinicId = ClinicEyes,
            DeferReason = "سبب",
            DeferUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            ActionTypeId = 1,
            FollowupNote = "ن",
            Status = OperationalReportsService.DeferredStatusRequested,
            RequestedByUserId = "actor-1",
            RequestedAt = DateTimeOffset.UtcNow
        });
        await SeedAssessmentAsync(db, deferredMember, CenterA, ClinicEyes, 1);
        await db.SaveChangesAsync();

        var local = CreateService(db, CenterA);
        var central = CreateService(db, CenterA, allowCentral: true);

        Assert.NotEmpty((await central.ExportCentralKpisAsync(new CentralKpiQuery())).FileContents);
        Assert.NotEmpty((await local.ExportFinalMembersAsync(new FinalMembersQuery())).FileContents);
        Assert.NotEmpty((await local.ExportDeferredReportAsync(new DeferredReportQuery())).FileContents);
        Assert.NotEmpty((await local.ExportLocalSummaryAsync(new LocalSummaryQuery())).FileContents);
        Assert.NotEmpty((await central.ExportCentralCompareAsync(new CentralCompareQuery())).FileContents);
        Assert.NotEmpty((await local.ExportDoctorLinkageAsync(new DoctorLinkageQuery())).FileContents);
    }

    [Fact]
    public async Task Inclusive_date_range_includes_both_ends()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberWithDecisionAsync(db, CenterA, "INC-1", 1);
        var member = await db.Members.SingleAsync(m => m.MemberId == memberId);
        member.CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        var decision = await db.FinalDecisions.SingleAsync(d => d.MemberId == memberId);
        decision.DecidedAt = DateTimeOffset.Parse("2026-03-15T12:00:00Z");
        await db.SaveChangesAsync();

        var onDay = await CreateService(db, CenterA).GetLocalKpisAsync(new LocalKpiQuery
        {
            FromDate = new DateOnly(2026, 3, 15),
            ToDate = new DateOnly(2026, 3, 15)
        });
        Assert.Equal(1, onDay.TotalMembers);
        Assert.Equal(1, onDay.AcceptedCount);

        var outside = await CreateService(db, CenterA).GetLocalKpisAsync(new LocalKpiQuery
        {
            FromDate = new DateOnly(2026, 3, 16),
            ToDate = new DateOnly(2026, 3, 16)
        });
        Assert.Equal(0, outside.TotalMembers);
    }

    [Fact]
    public async Task FinalMembers_period_filters_by_decidedAt_not_createdAt()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        var earlyCreateLateDecide = await CreateMemberWithDecisionAsync(db, CenterA, "DEC-1", 1);
        var m1 = await db.Members.SingleAsync(x => x.MemberId == earlyCreateLateDecide);
        m1.CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
        var d1 = await db.FinalDecisions.SingleAsync(x => x.MemberId == earlyCreateLateDecide);
        d1.DecidedAt = DateTimeOffset.Parse("2026-11-01T10:00:00Z");

        var otherCenter = await CreateMemberWithDecisionAsync(db, CenterB, "DEC-B", 1);
        var dB = await db.FinalDecisions.SingleAsync(x => x.MemberId == otherCenter);
        dB.DecidedAt = DateTimeOffset.Parse("2026-11-01T11:00:00Z");

        var outOfRange = await CreateMemberWithDecisionAsync(db, CenterA, "DEC-2", 1);
        var d2 = await db.FinalDecisions.SingleAsync(x => x.MemberId == outOfRange);
        d2.DecidedAt = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
        await db.SaveChangesAsync();

        var page = await CreateService(db, CenterA).ListFinalMembersAsync(new FinalMembersQuery
        {
            FromDate = new DateOnly(2026, 10, 30),
            ToDate = new DateOnly(2026, 11, 3),
            PageNumber = 1,
            PageSize = 20
        });

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(earlyCreateLateDecide, Assert.Single(page.Items).MemberId);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T10:00:00Z"), page.Items.Single().DecidedAt);
    }

    [Fact]
    public async Task LocalSummary_period_aligns_decision_counts_with_decidedAt()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        var decided = await CreateMemberWithDecisionAsync(db, CenterA, "SUM-DEC", 1);
        var m = await db.Members.SingleAsync(x => x.MemberId == decided);
        m.CreatedAt = DateTimeOffset.Parse("2025-06-01T00:00:00Z");
        var d = await db.FinalDecisions.SingleAsync(x => x.MemberId == decided);
        d.DecidedAt = DateTimeOffset.Parse("2026-11-02T08:00:00Z");
        await db.SaveChangesAsync();

        var summary = await CreateService(db, CenterA).GetLocalSummaryAsync(new LocalSummaryQuery
        {
            FromDate = new DateOnly(2026, 10, 30),
            ToDate = new DateOnly(2026, 11, 3)
        });

        Assert.Equal(1, summary.TotalMembers);
        Assert.Equal(1, summary.AcceptedCount);
        Assert.Equal(0, summary.WaitingCount);
    }

    [Fact]
    public async Task DoctorLinkage_returns_final_decision_and_filters_clinic_result()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var memberId = await CreateMemberWithDecisionAsync(db, CenterA, "DOC-FIN", 2, "رفض");

        var acceptedClinicOnly = await CreateService(db, CenterA).ListDoctorLinkageByMemberAsync(
            new DoctorLinkageQuery { ResultStatusId = 1, ViewMode = DoctorLinkageViewModes.ByMember });
        Assert.DoesNotContain(acceptedClinicOnly.Items, i => i.MemberId == memberId);

        var rejectedClinic = await CreateService(db, CenterA).ListDoctorLinkageByMemberAsync(
            new DoctorLinkageQuery { ResultStatusId = 2, ViewMode = DoctorLinkageViewModes.ByMember });
        var filtered = Assert.Single(rejectedClinic.Items.Where(i => i.MemberId == memberId));
        Assert.All(filtered.Exams, e => Assert.Equal((short)2, e.ResultStatusId));
        Assert.Equal((short)2, filtered.FinalResultStatusId);
        Assert.NotNull(filtered.DecidedAt);
        Assert.False(string.IsNullOrWhiteSpace(filtered.FinalResultNameAr));
    }

    [Fact]
    public async Task WaitingMembers_rejects_missing_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);
        var service = CreateService(db, CenterA, allowLocal: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ListWaitingMembersAsync(new WaitingMembersQuery()));
    }

    [Fact]
    public async Task WaitingMembers_matches_waitingCount_and_excludes_decided()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        await CreateMemberWithDecisionAsync(db, CenterA, "WAIT-ACC", 1);
        var waitingId = await CreateMemberAsync(db, CenterA, "WAIT-ONLY");
        await CreateMemberAsync(db, CenterB, "WAIT-B");

        var service = CreateService(db, CenterA);
        var kpi = await service.GetLocalKpisAsync(new LocalKpiQuery());
        var page = await service.ListWaitingMembersAsync(new WaitingMembersQuery
        {
            PageNumber = 1,
            PageSize = 20
        });

        Assert.Equal(kpi.WaitingCount, page.TotalCount);
        Assert.Equal(1, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal(waitingId, item.MemberId);
        Assert.Null(item.FinalResultStatusId);
        Assert.Null(item.DecidedAt);
        Assert.Null(item.FinalResultNameAr);
    }

    [Fact]
    public async Task WaitingMembers_period_filters_by_createdAt()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        var inRange = await CreateMemberAsync(db, CenterA, "WAIT-IN");
        var mIn = await db.Members.SingleAsync(x => x.MemberId == inRange);
        mIn.CreatedAt = DateTimeOffset.Parse("2026-11-01T10:00:00Z");

        var outOfRange = await CreateMemberAsync(db, CenterA, "WAIT-OUT");
        var mOut = await db.Members.SingleAsync(x => x.MemberId == outOfRange);
        mOut.CreatedAt = DateTimeOffset.Parse("2026-10-01T10:00:00Z");

        // قرار في الفترة على DecidedAt لا يُدخل المنتسب في قائمة الانتظار
        var decided = await CreateMemberWithDecisionAsync(db, CenterA, "WAIT-DEC", 1);
        var mDec = await db.Members.SingleAsync(x => x.MemberId == decided);
        mDec.CreatedAt = DateTimeOffset.Parse("2026-11-01T09:00:00Z");
        var d = await db.FinalDecisions.SingleAsync(x => x.MemberId == decided);
        d.DecidedAt = DateTimeOffset.Parse("2026-11-02T08:00:00Z");
        await db.SaveChangesAsync();

        var service = CreateService(db, CenterA);
        var range = new WaitingMembersQuery
        {
            FromDate = new DateOnly(2026, 10, 30),
            ToDate = new DateOnly(2026, 11, 3),
            PageNumber = 1,
            PageSize = 20
        };
        var page = await service.ListWaitingMembersAsync(range);
        var kpi = await service.GetLocalKpisAsync(new LocalKpiQuery
        {
            FromDate = range.FromDate,
            ToDate = range.ToDate
        });

        Assert.Equal(kpi.WaitingCount, page.TotalCount);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(inRange, Assert.Single(page.Items).MemberId);
    }

    [Fact]
    public async Task WaitingMembers_paginates_and_filters_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        await SeedAsync(db);

        var withEyes = await CreateMemberAsync(db, CenterA, "WAIT-EYE");
        await SeedAssessmentAsync(db, withEyes, CenterA, ClinicEyes, 1);

        var withEntOnly = await CreateMemberAsync(db, CenterA, "WAIT-ENT");
        await SeedAssessmentAsync(db, withEntOnly, CenterA, ClinicEnt, 1);

        await CreateMemberAsync(db, CenterA, "WAIT-NONE");

        var page1 = await CreateService(db, CenterA).ListWaitingMembersAsync(new WaitingMembersQuery
        {
            ClinicId = ClinicEyes,
            PageNumber = 1,
            PageSize = 1
        });

        Assert.Equal(1, page1.TotalCount);
        Assert.Equal(withEyes, Assert.Single(page1.Items).MemberId);
        Assert.Equal(1, page1.Items.Count);
    }

    private static OperationalReportsService CreateService(
        AppDbContext db,
        Guid centerId,
        bool allowLocal = true,
        bool allowCentral = false,
        Mock<ICrossCenterAccessService>? cross = null,
        Mock<IPermissionGate>? gate = null,
        bool configureGate = true)
    {
        gate ??= new Mock<IPermissionGate>();
        if (configureGate)
        {
            if (allowLocal)
            {
                gate.Setup(g => g.EnsureLocalAsync(
                        MilitaryHealthPermissions.Reports.Local,
                        It.IsAny<Guid?>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(centerId);
            }
            else
            {
                gate.Setup(g => g.EnsureLocalAsync(
                        MilitaryHealthPermissions.Reports.Local,
                        It.IsAny<Guid?>(),
                        It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new ForbiddenPermissionException());
            }

            if (allowCentral)
            {
                gate.Setup(g => g.EnsureCentralAsync(
                        MilitaryHealthPermissions.Reports.Central,
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
            }
            else
            {
                gate.Setup(g => g.EnsureCentralAsync(
                        MilitaryHealthPermissions.Reports.Central,
                        It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new ForbiddenPermissionException());
            }
        }

        var currentUser = new TestCurrentUser();
        currentUser.SetUser("actor-1", "actor", "127.0.0.1");

        cross ??= new Mock<ICrossCenterAccessService>();
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
            new ReportsTestUnitOfWork(db));
    }

    private static async Task<Guid> CreateMemberWithDecisionAsync(
        AppDbContext db,
        Guid centerId,
        string recruitment,
        short resultStatusId,
        string? rejectionReason = null)
    {
        var memberId = await CreateMemberAsync(db, centerId, recruitment);
        await SeedAllAssessmentsAsync(db, memberId, centerId, resultStatusId == 3 ? (short)1 : resultStatusId);
        var now = DateTimeOffset.UtcNow;
        var decisionId = Guid.NewGuid();
        db.FinalDecisions.Add(new FinalDecision
        {
            FinalDecisionId = decisionId,
            MemberId = memberId,
            CenterId = centerId,
            SupervisorUserId = "actor-1",
            ResultStatusId = resultStatusId,
            RejectionReason = rejectionReason,
            DecidedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
        if (resultStatusId == 3)
        {
            db.DeferredActions.Add(new DeferredAction
            {
                DeferredActionId = Guid.NewGuid(),
                MemberId = memberId,
                CenterId = centerId,
                FinalDecisionId = decisionId,
                DeferReason = rejectionReason ?? "مؤجل",
                DeferUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
                ActionTypeId = 1,
                FollowupNote = "متابعة",
                Status = OperationalReportsService.DeferredStatusRequested,
                RequestedByUserId = "actor-1",
                RequestedAt = now
            });
        }

        await db.SaveChangesAsync();
        return memberId;
    }

    private static async Task ArchiveMemberAsync(AppDbContext db, Guid memberId, Guid centerId)
    {
        var decision = await db.FinalDecisions.SingleAsync(d =>
            d.MemberId == memberId && d.CenterId == centerId);
        var center = await db.MedicalCenters.SingleAsync(c => c.CenterId == centerId);
        var seq = await db.MemberArchives.CountAsync(a => a.CenterId == centerId) + 1;
        var now = DateTimeOffset.UtcNow;
        db.MemberArchives.Add(new MemberArchive
        {
            ArchiveId = Guid.NewGuid(),
            MemberId = memberId,
            FinalDecisionId = decision.FinalDecisionId,
            CenterId = centerId,
            ArchiveNumber = $"{center.Code}-{seq:D8}",
            ArchivedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CreateMemberAsync(AppDbContext db, Guid centerId, string recruitment)
    {
        var members = MemberServiceTests.CreateService(db, centerId, allowCreate: true, allowView: true);
        var created = await members.CreateAsync(new CreateMemberRequest
        {
            FullName = $"منتسب {recruitment}",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-22),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = recruitment,
            RecruitmentCenterId = RecruitmentCenterId,
            BloodTypeId = 1,
            HeightCm = 170,
            WeightKg = 70,
            BloodPressure = "120/80",
            PulseBpm = 72,
            HasTattoo = false
        });
        return created.MemberId;
    }

    private static async Task SeedAllAssessmentsAsync(
        AppDbContext db, Guid memberId, Guid centerId, short resultStatusId)
    {
        foreach (var clinicId in AllClinics)
            await SeedAssessmentAsync(db, memberId, centerId, clinicId, resultStatusId);
    }

    private static async Task SeedAssessmentAsync(
        AppDbContext db, Guid memberId, Guid centerId, Guid clinicId, short resultStatusId)
    {
        var examId = Guid.NewGuid();
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
            AssessmentId = Guid.NewGuid(),
            ExamId = examId,
            MemberId = memberId,
            CenterId = centerId,
            ClinicId = clinicId,
            ResultStatusId = resultStatusId,
            RejectionReason = resultStatusId == 2 ? "سبب" : null,
            ClinicalNotes = "ملاحظة",
            AssessedByUserId = "actor-1",
            AssessedAt = now,
            UpdatedByUserId = "actor-1",
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAsync(AppDbContext db)
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
            if (!await db.Clinics.AnyAsync(c => c.ClinicId == id))
            {
                db.Clinics.Add(new Clinic
                {
                    ClinicId = id,
                    Code = code,
                    NameAr = name,
                    SortOrder = (short)sort,
                    CountsForFinal = true,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync();
        foreach (var clinicId in AllClinics)
        {
            foreach (var centerId in new[] { CenterA, CenterB })
            {
                if (!await db.CenterClinics.AnyAsync(x => x.CenterId == centerId && x.ClinicId == clinicId))
                {
                    db.CenterClinics.Add(new CenterClinic
                    {
                        CenterClinicId = Guid.NewGuid(),
                        CenterId = centerId,
                        ClinicId = clinicId,
                        IsActive = true,
                        ActivatedAt = DateTimeOffset.UtcNow
                    });
                }
            }
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
                Code = "FORM",
                NameAr = "طلب استمارة",
                RequiresDescription = false,
                RequiresTargetClinic = false,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed class ReportsTestUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        public ReportsTestUnitOfWork(AppDbContext db) => _db = db;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => _db.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new NoopTx());
    }

    private sealed class NoopTx : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
