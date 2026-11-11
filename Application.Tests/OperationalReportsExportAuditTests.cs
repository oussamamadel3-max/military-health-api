using System.Text.Json;
using Application.Common.Permissions;
using Application.Common.Security;
using Application.Dtos.OperationalReports;
using Application.Exceptions;
using Application.ModelsDto.AuditLogDtos;
using Application.Services;
using Application.Services.OperationalReports;
using Domain.Entities.Audit;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>BUG-F068-003: سياق تدقيق تصدير التقارير التشغيلية (محلي CENTER / مركزي CENTRAL).</summary>
public class OperationalReportsExportAuditTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClinicEyes = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Local_export_records_CENTER_scope_with_active_center_and_record_count()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        await OperationalReportsServiceTests.CreateMemberWithDecisionAsync(db, CenterA, "AUD-FM", 1);
        await OperationalReportsServiceTests.CreateMemberWithDecisionAsync(db, CenterA, "AUD-FM-2", 2, "رفض");

        var service = OperationalReportsServiceTests.CreateService(db, CenterA);
        var before = await db.AuditEvents.CountAsync(e => e.EventType == OperationalReportsService.ExportEventType);

        var file = await service.ExportFinalMembersAsync(new FinalMembersQuery());
        Assert.NotEmpty(file.FileContents);

        var audit = await db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == OperationalReportsService.ExportEventType)
            .OrderByDescending(e => e.EventId)
            .FirstAsync();

        Assert.Equal(before + 1, await db.AuditEvents.CountAsync(e => e.EventType == OperationalReportsService.ExportEventType));
        Assert.Equal(CenterClaimTypes.Center, audit.ContextScope);
        Assert.Equal(CenterA, audit.CenterId);
        Assert.Equal("actor-1", audit.ActorUserId);
        Assert.Equal("OperationalReport", audit.EntityType);
        Assert.Null(audit.EntityId);

        using var doc = JsonDocument.Parse(audit.NewValues!);
        Assert.True(doc.RootElement.TryGetProperty("RecordCount", out var rc));
        Assert.Equal(2, rc.GetInt32());
        Assert.Equal("القائمة-النهائية", doc.RootElement.GetProperty("reportName").GetString());

        var newValues = audit.NewValues ?? "";
        Assert.DoesNotContain("AUD-FM", newValues, StringComparison.Ordinal);
        Assert.DoesNotContain("منتسب", newValues, StringComparison.Ordinal);
        Assert.DoesNotContain("FileNumber", newValues, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Central_export_records_CENTRAL_scope_with_null_center()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        await OperationalReportsServiceTests.CreateMemberWithDecisionAsync(db, CenterA, "AUD-CEN", 1);

        var service = OperationalReportsServiceTests.CreateService(db, CenterA, allowCentral: true);
        await service.ExportCentralCompareAsync(new CentralCompareQuery());

        var exportAudit = Assert.Single(await db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == OperationalReportsService.ExportEventType)
            .ToListAsync());

        Assert.Equal(CenterClaimTypes.Central, exportAudit.ContextScope);
        Assert.Null(exportAudit.CenterId);
    }

    [Fact]
    public async Task Deferred_local_export_uses_verified_center_in_audit()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        var memberId = await OperationalReportsServiceTests.CreateMemberWithDecisionAsync(db, CenterA, "AUD-DEF", 3, "مؤجل");
        var clinicDefer = await db.DeferredActions.FirstAsync(d => d.MemberId == memberId);
        clinicDefer.ClinicId = ClinicEyes;
        clinicDefer.ActionDescription = "وصف";
        await db.SaveChangesAsync();

        var service = OperationalReportsServiceTests.CreateService(db, CenterA);
        await service.ExportDeferredReportAsync(new DeferredReportQuery());

        var audit = Assert.Single(await db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == OperationalReportsService.ExportEventType)
            .ToListAsync());

        Assert.Equal(CenterA, audit.CenterId);
        Assert.Equal(CenterClaimTypes.Center, audit.ContextScope);
    }

    [Fact]
    public async Task AuditEventQuery_center_filter_returns_only_that_centers_export_events()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        await OperationalReportsServiceTests.CreateMemberWithDecisionAsync(db, CenterA, "AUD-Q-A", 1);
        await OperationalReportsServiceTests.CreateMemberWithDecisionAsync(db, CenterB, "AUD-Q-B", 1);

        await OperationalReportsServiceTests.CreateService(db, CenterA).ExportFinalMembersAsync(new FinalMembersQuery());
        await OperationalReportsServiceTests.CreateService(db, CenterB).ExportFinalMembersAsync(new FinalMembersQuery());

        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = "actor-1",
            ContextScope = CenterClaimTypes.Central,
            CenterId = null,
            EventType = OperationalReportsService.ExportEventType,
            Result = "SUCCESS",
            EntityType = "OperationalReport",
            CorrelationId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var users = new UserRepository(db);
        var scoped = await AuditEventQuery.ApplyAsync(
            db.AuditEvents.AsNoTracking(),
            new AuditLogFilter
            {
                centerId = CenterA,
                eventType = OperationalReportsService.ExportEventType
            },
            users);

        var list = await scoped.ToListAsync();
        Assert.Single(list);
        Assert.Equal(CenterA, list[0].CenterId);
    }

    [Fact]
    public async Task Denied_export_does_not_write_success_export_audit()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        await OperationalReportsServiceTests.CreateMemberWithDecisionAsync(db, CenterA, "AUD-DENY", 1);

        var service = OperationalReportsServiceTests.CreateService(db, CenterA, allowMemberDetailView: false);
        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ExportFinalMembersAsync(new FinalMembersQuery()));

        Assert.Empty(await db.AuditEvents
            .Where(e => e.EventType == OperationalReportsService.ExportEventType)
            .ToListAsync());
    }

    [Fact]
    public async Task Empty_export_throws_and_does_not_record_export_audit()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);

        var service = OperationalReportsServiceTests.CreateService(db, CenterA);
        await Assert.ThrowsAsync<OperationalReportExportEmptyException>(() =>
            service.ExportFinalMembersAsync(new FinalMembersQuery()));

        Assert.Empty(await db.AuditEvents
            .Where(e => e.EventType == OperationalReportsService.ExportEventType)
            .ToListAsync());
    }

    [Fact]
    public async Task Central_kpi_view_audit_remains_CENTRAL_after_fix()
    {
        await using var db = MemberServiceTests.CreateDb();
        await OperationalReportsServiceTests.SeedAsync(db);
        await OperationalReportsServiceTests.CreateMemberWithDecisionAsync(db, CenterA, "AUD-VIEW", 1);

        var service = OperationalReportsServiceTests.CreateService(db, CenterA, allowCentral: true);
        await service.GetCentralKpisAsync(new CentralKpiQuery());

        var view = Assert.Single(await db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == OperationalReportsService.CentralViewEventType)
            .ToListAsync());

        Assert.Equal(CenterClaimTypes.Central, view.ContextScope);
        Assert.Null(view.CenterId);
    }
}
