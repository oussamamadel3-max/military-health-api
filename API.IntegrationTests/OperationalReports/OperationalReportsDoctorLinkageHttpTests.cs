using System.Net;
using System.Text.Json;
using API.IntegrationTests.Infrastructure;
using Application.Common.Security;
using Application.Services.OperationalReports;
using Microsoft.EntityFrameworkCore;

namespace API.IntegrationTests.OperationalReports;

/// <summary>FR-068: صلاحيات HTTP لربط الأطباء (A2) وعزل المركز.</summary>
[Collection(AuthHttp091Collection.Name)]
public sealed class OperationalReportsDoctorLinkageHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly AuthWebApplicationFactory _factory;

    public OperationalReportsDoctorLinkageHttpTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Doctor_linkage_http_matrix_reports_local_member_view_and_center_binding()
    {
        await OperationalReportsHttpSeed.EnsureDoctorLinkageHttpFixturesAsync(_factory.Services);

        var indicators = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await indicators.LoginAsync(OperationalReportsHttpSeed.IndicatorsUserName,
                OperationalReportsHttpSeed.ReportsTestPassword)).StatusCode);

        var byDoctor = await indicators.Client.GetAsync(
            "/api/operational-reports/local/doctor-linkage?ViewMode=BY_DOCTOR");
        Assert.Equal(HttpStatusCode.OK, byDoctor.StatusCode);
        var byDoctorJson = await byDoctor.Content.ReadAsStringAsync();
        Assert.DoesNotContain("fileNumber", byDoctorJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullName", byDoctorJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memberName", byDoctorJson, StringComparison.OrdinalIgnoreCase);

        var byMemberDenied = await indicators.Client.GetAsync("/api/operational-reports/local/doctor-linkage");
        Assert.Equal(HttpStatusCode.Forbidden, byMemberDenied.StatusCode);

        var exportDeniedCount = await CountExportAuditsAsync();
        var exportDenied = await indicators.Client.GetAsync("/api/operational-reports/local/doctor-linkage/export");
        Assert.Equal(HttpStatusCode.Forbidden, exportDenied.StatusCode);
        Assert.Equal(exportDeniedCount, await CountExportAuditsAsync());

        var mismatch = await indicators.Client.GetAsync(
            $"/api/operational-reports/local/doctor-linkage?ViewMode=BY_DOCTOR&centerId={OperationalReportsHttpSeed.CenterA}");
        Assert.Equal(HttpStatusCode.Forbidden, mismatch.StatusCode);

        var full = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK,
            (await full.LoginAsync(OperationalReportsHttpSeed.FullAccessUserName,
                OperationalReportsHttpSeed.ReportsTestPassword)).StatusCode);

        var byMemberOk = await full.Client.GetAsync("/api/operational-reports/local/doctor-linkage");
        Assert.Equal(HttpStatusCode.OK, byMemberOk.StatusCode);
        var memberBody = await byMemberOk.Content.ReadAsStringAsync();
        Assert.Contains("fullName", memberBody, StringComparison.OrdinalIgnoreCase);

        var beforeExport = await CountExportAuditsAsync();
        var exportOk = await full.Client.GetAsync("/api/operational-reports/local/doctor-linkage/export");
        Assert.Equal(HttpStatusCode.OK, exportOk.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            exportOk.Content.Headers.ContentType?.MediaType);
        var bytes = await exportOk.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 100);
        Assert.True(bytes[0] == 0x50 && bytes[1] == 0x4B, "Expected XLSX zip (PK) header.");

        var audit = await _factory.WithDbAsync(db => db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == OperationalReportsService.ExportEventType)
            .OrderByDescending(e => e.EventId)
            .FirstAsync());
        Assert.Equal(beforeExport + 1, await CountExportAuditsAsync());
        Assert.Equal(CenterClaimTypes.Center, audit.ContextScope);
        Assert.Equal(OperationalReportsHttpSeed.CenterB, audit.CenterId);
    }

    private Task<int> CountExportAuditsAsync()
        => _factory.WithDbAsync(db => db.AuditEvents.CountAsync(e =>
            e.EventType == OperationalReportsService.ExportEventType));
}
