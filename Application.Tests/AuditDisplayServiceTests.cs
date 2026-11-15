using System.Text.Json;
using Application.Dtos.ExportDtos;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.ModelsDto.AuditLogDtos;
using Application.Services;
using Application.Services.Reports;
using Domain.Entities.Audit;
using Domain.Entities.Auth;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;

namespace Application.Tests;

public class AuditDisplayServiceTests
{
    [Fact]
    public async Task List_keeps_raw_fields_and_shows_current_center_name_with_snapshot_entity_name()
    {
        await using var db = MemberServiceTests.CreateDb();
        var centerId = Guid.NewGuid();
        db.MedicalCenters.Add(Center(centerId, "الاسم الحالي"));
        db.Users.Add(new User
        {
            Id = "actor-1",
            UserName = "actor1",
            NormalizedUserName = "ACTOR1",
            Email = "a@test.local",
            NormalizedEmail = "A@TEST.LOCAL",
            FullName = "المستخدم الحالي",
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        });
        db.AuditEvents.Add(Event(centerId, "Logout", "SUCCESS", JsonSerializer.Serialize(new { NameAr = "الاسم وقت الحدث" })));
        await db.SaveChangesAsync();

        var page = await CreateService(db).GetAllFilteredAsync(1, 10, new AuditLogFilter { eventType = "Logout" });
        var row = Assert.Single(page.Items);

        Assert.Equal("Logout", row.ActionType);
        Assert.Equal("Member", row.TableName);
        Assert.Equal("SUCCESS", row.Result);
        Assert.Equal("الاسم الحالي", row.CenterName);
        Assert.Equal("الاسم وقت الحدث", row.EntityDisplayName);
        Assert.Equal(centerId, row.CenterId);
    }

    [Fact]
    public async Task Export_excel_and_pdf_use_the_same_readable_row()
    {
        var excel = await ExportOnce("excel");
        var pdf = await ExportOnce("pdf");

        Assert.Equal("المنتسب", excel.Row.TableName);
        Assert.Equal(excel.Row.TableName, pdf.Row.TableName);
        Assert.Equal("تسجيل خروج", excel.Row.ActionType);
        Assert.Equal("فشل", excel.Row.Result);
        Assert.Equal("مركز العرض", excel.Row.CenterName);
        Assert.Equal("—", excel.Row.EntityDisplayName);
        Assert.DoesNotContain(excel.EntityId.ToString(), excel.Row.EntityDisplayName ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(excel.CenterId.ToString(), excel.Row.CenterName, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(excel.Options.ColumnMappings);
        Assert.Equal("اسم المركز الحالي", excel.Options.ColumnMappings["CenterName"]);
        Assert.DoesNotContain(excel.Options.ColumnMappings.Values, value => value.Contains("معرف", StringComparison.Ordinal));
    }

    private static async Task<ExportCapture> ExportOnce(string format)
    {
        await using var db = MemberServiceTests.CreateDb();
        var centerId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        db.MedicalCenters.Add(Center(centerId, "مركز العرض"));
        db.AuditEvents.Add(Event(centerId, "Logout", "FAILURE", null, entityId));
        await db.SaveChangesAsync();

        ExportCapture? captured = null;
        var export = new Mock<IExportService>();
        export.Setup(service => service.ExportToExcelAsync(
                It.IsAny<IEnumerable<Application.Dtos.ReportDtos.AuditLogReportRowDto>>(),
                It.IsAny<ExportOptions>()))
            .Callback<IEnumerable<Application.Dtos.ReportDtos.AuditLogReportRowDto>, ExportOptions>((rows, options) =>
                captured = new ExportCapture(Assert.Single(rows), options, centerId, entityId))
            .ReturnsAsync(new ExportFileDto { FileContents = [1], ContentType = "application/vnd.ms-excel", FileName = "a.xlsx" });
        export.Setup(service => service.ExportToPdfAsync(
                It.IsAny<IEnumerable<Application.Dtos.ReportDtos.AuditLogReportRowDto>>(),
                It.IsAny<ExportOptions>()))
            .Callback<IEnumerable<Application.Dtos.ReportDtos.AuditLogReportRowDto>, ExportOptions>((rows, options) =>
                captured = new ExportCapture(Assert.Single(rows), options, centerId, entityId))
            .ReturnsAsync(new ExportFileDto { FileContents = [1], ContentType = "application/pdf", FileName = "a.pdf" });

        await CreateReports(db, export.Object).ExportAuditLogsAsync(new AuditLogFilter { eventType = "Logout" }, format);
        return captured!;
    }

    private static MedicalCenter Center(Guid id, string name) => new()
    {
        CenterId = id,
        Code = id.ToString("N")[..8],
        NameAr = name,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static AuditEvent Event(Guid centerId, string eventType, string result, string? newValues, Guid? entityId = null)
        => new()
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = "actor-1",
            CenterId = centerId,
            EventType = eventType,
            Result = result,
            EntityType = "Member",
            EntityId = (entityId ?? Guid.NewGuid()).ToString(),
            NewValues = newValues,
            CorrelationId = Guid.NewGuid()
        };

    private static AuditLogService CreateService(AppDbContext db)
    {
        var config = new TypeAdapterConfig();
        new Application.Mappings.AuditLogProfile().Register(config);
        var gate = new Mock<IPermissionGate>();
        gate.Setup(item => item.EnsureAll(It.IsAny<string[]>()));
        return new AuditLogService(
            new AuditLogRepository(db),
            new UserRepository(db),
            new Mapper(config),
            gate.Object,
            new MedicalCenterRepository(db));
    }

    private static ReportsService CreateReports(AppDbContext db, IExportService export)
    {
        var gate = new Mock<IPermissionGate>();
        gate.Setup(item => item.EnsureAll(It.IsAny<string[]>()));
        return new ReportsService(
            new AuditLogRepository(db),
            new UserRepository(db),
            export,
            Mock.Of<IDashboardService>(),
            gate.Object,
            new TestCurrentUser(),
            new DisplayTestUnitOfWork(db),
            new MedicalCenterRepository(db));
    }

    private sealed record ExportCapture(
        Application.Dtos.ReportDtos.AuditLogReportRowDto Row,
        ExportOptions Options,
        Guid CenterId,
        Guid EntityId);

    private sealed class DisplayTestUnitOfWork(AppDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => db.SaveChangesAsync(cancellationToken);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => db.Database.BeginTransactionAsync(cancellationToken);
    }
}
