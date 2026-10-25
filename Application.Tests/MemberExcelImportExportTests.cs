using Application.Common.Permissions;
using Application.Dtos.Members;
using Application.Exceptions;
using Application.Services.Members;
using ClosedXML.Excel;
using Domain.Entities.Patient;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>اختبارات قبول استيراد/تصدير Excel للمنتسبين (MH-P3-090 / MH-P3-110).</summary>
public class MemberExcelImportExportTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RecruitmentCenterId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task Import_template_download_returns_xlsx_with_required_headers()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(db, CenterA, allowCreate: false, allowView: false, allowImport: true);

        var file = await service.GetImportTemplateAsync();

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Contains("قالب", file.FileName);
        using var ms = new MemoryStream(file.FileContents);
        using var wb = new XLWorkbook(ms);
        var headers = wb.Worksheet(1).Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        foreach (var required in MemberImportExcel.RequiredHeaders)
            Assert.Contains(required, headers);
        Assert.DoesNotContain("رقم الملف", headers);
        Assert.DoesNotContain("رقم الدور", headers);
    }

    [Fact]
    public async Task Import_preview_does_not_create_members()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(
            db, CenterA, allowCreate: true, allowView: true, allowImport: true);

        var form = BuildImportFile(ValidRow("IMP-1"));
        var preview = await service.PreviewImportAsync(form);

        Assert.Equal(1, preview.ValidCount);
        var valid = Assert.Single(preview.ValidRows);
        Assert.Equal("منتسب استيراد", valid.FullName);
        Assert.Equal("IMP-1", valid.RecruitmentNumber);
        Assert.Equal("تجنيد 1", valid.RecruitmentCenterNameAr);
        Assert.Equal(0, await db.Members.CountAsync());
        Assert.Equal(1, await db.MemberImportBatches.CountAsync());
        Assert.Equal(MemberImportBatchStatuses.Previewed, (await db.MemberImportBatches.SingleAsync()).Status);
    }

    [Fact]
    public async Task Import_preview_rejects_missing_columns()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(db, CenterA, allowCreate: false, allowView: false, allowImport: true);

        using var wb = new XLWorkbook();
        var sheet = wb.Worksheets.Add("x");
        sheet.Cell(1, 1).Value = "الاسم الكامل";
        sheet.Cell(2, 1).Value = "اسم";
        var form = ToFormFile(wb, "bad.xlsx");

        var error = await Assert.ThrowsAsync<MemberImportException>(() => service.PreviewImportAsync(form));
        Assert.Contains("غير مطابق", error.Message);
    }

    [Fact]
    public async Task Import_preview_flags_internal_and_db_duplicates()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(
            db, CenterA, allowCreate: true, allowView: true, allowImport: true);

        await service.CreateAsync(new CreateMemberRequest
        {
            FullName = "موجود",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-20),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = "EXIST-1",
            RecruitmentCenterId = RecruitmentCenterId,
            HeightCm = 170,
            WeightKg = 70
        });

        var form = BuildImportFile(
            ValidRow("DUP-IN"),
            ValidRow("DUP-IN", fullName: "ثاني"),
            ValidRow("EXIST-1", fullName: "من النظام"));

        var preview = await service.PreviewImportAsync(form);

        Assert.Equal(1, preview.ValidCount);
        Assert.Equal(2, preview.DuplicateCount);
        Assert.Single(preview.ValidRows);
        Assert.Contains(preview.Issues, i =>
            i.Status == "Duplicate"
            && i.Reason.Contains("داخل الملف")
            && i.FullName == "ثاني"
            && i.RecruitmentNumber == "DUP-IN"
            && i.Errors.Count >= 1);
        Assert.Contains(preview.Issues, i =>
            i.Status == "Duplicate"
            && i.Reason.Contains("مسجّل مسبقاً")
            && i.FullName == "من النظام"
            && i.RecruitmentNumber == "EXIST-1"
            && i.Errors.Count >= 1);
    }

    [Fact]
    public async Task Import_preview_flags_invalid_measurement_or_date()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(db, CenterA, allowCreate: false, allowView: false, allowImport: true);

        var bad = ValidRow("BAD-1");
        bad["تاريخ الميلاد"] = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
        var form = BuildImportFile(bad);

        var preview = await service.PreviewImportAsync(form);
        Assert.Equal(0, preview.ValidCount);
        Assert.Equal(1, preview.InvalidCount);
    }

    [Fact]
    public async Task Import_commit_creates_valid_only_and_rejects_second_commit()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(
            db, CenterA, allowCreate: true, allowView: true, allowImport: true);

        var form = BuildImportFile(ValidRow("OK-1"), ValidRow("OK-2", fullName: "ثاني"));
        var preview = await service.PreviewImportAsync(form);
        Assert.Equal(2, preview.ValidCount);

        var commit = await service.CommitImportAsync(preview.ImportId);
        Assert.Equal(2, commit.CommittedCount);
        Assert.Equal(2, await db.Members.CountAsync());
        var members = await db.Members.OrderBy(m => m.FileNumber).ToListAsync();
        Assert.All(members, m => Assert.Equal(CenterA, m.CurrentMedicalCenterId));
        Assert.Equal(2, members.Select(m => m.FileNumber).Distinct().Count());
        Assert.Equal(2, members.Select(m => m.QueueNumber).Distinct().Count());
        Assert.Equal(members[0].FileNumber + 1, members[1].FileNumber);
        Assert.Equal(members[0].QueueNumber + 1, members[1].QueueNumber);
        Assert.Equal(commit.CreatedFileNumbers, members.Select(m => m.FileNumber).ToList());
        Assert.Equal(MemberImportBatchStatuses.Committed, (await db.MemberImportBatches.SingleAsync()).Status);

        var audit = await db.AuditEvents.SingleAsync(e => e.EventType == "MemberImport");
        Assert.Equal("SUCCESS", audit.Result);

        await Assert.ThrowsAsync<MemberImportAlreadyCommittedException>(() =>
            service.CommitImportAsync(preview.ImportId));
    }

    [Fact]
    public async Task Import_cancel_leaves_no_members()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(db, CenterA, allowCreate: false, allowView: false, allowImport: true);

        var preview = await service.PreviewImportAsync(BuildImportFile(ValidRow("CX-1")));
        await service.CancelImportAsync(preview.ImportId);

        Assert.Equal(0, await db.Members.CountAsync());
        Assert.Equal(MemberImportBatchStatuses.Cancelled, (await db.MemberImportBatches.SingleAsync()).Status);
    }

    [Fact]
    public async Task Import_denied_without_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(db, CenterA, allowCreate: false, allowView: false, allowImport: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.GetImportTemplateAsync());
    }

    [Fact]
    public async Task Export_matches_filter_and_writes_audit()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(
            db, CenterA, allowCreate: true, allowView: true, allowExport: true);

        await service.CreateAsync(new CreateMemberRequest
        {
            FullName = "=SUM(1,1)",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-22),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = "EX-1",
            RecruitmentCenterId = RecruitmentCenterId,
            HeightCm = 170,
            WeightKg = 65
        });
        await service.CreateAsync(new CreateMemberRequest
        {
            FullName = "آخر",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-22),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = "EX-2",
            RecruitmentCenterId = RecruitmentCenterId
        });

        var file = await service.ExportAsync(new MemberListQuery { RecruitmentNumber = "EX-1" });
        Assert.EndsWith(".xlsx", file.FileName, StringComparison.OrdinalIgnoreCase);

        using var ms = new MemoryStream(file.FileContents);
        using var wb = new XLWorkbook(ms);
        var sheet = wb.Worksheet(1);
        var used = sheet.RangeUsed();
        Assert.NotNull(used);
        var values = used!.CellsUsed().Select(c => c.Value.ToString() ?? string.Empty).ToList();
        Assert.Contains(values, v => v.Contains("SUM", StringComparison.Ordinal));
        Assert.Contains(values, v => v.StartsWith("'", StringComparison.Ordinal) || v.Contains("'=SUM", StringComparison.Ordinal) || v == "'=SUM(1,1)" || v.Contains("=SUM"));

        Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.EventType == "MemberExport"));
    }

    [Fact]
    public async Task Export_empty_throws_without_silent_file()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(db, CenterA, allowCreate: false, allowView: false, allowExport: true);

        await Assert.ThrowsAsync<MemberExportEmptyException>(() =>
            service.ExportAsync(new MemberListQuery()));
    }

    [Fact]
    public async Task Export_isolates_centers()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);

        var serviceA = MemberServiceTests.CreateService(
            db, CenterA, allowCreate: true, allowView: true, allowExport: true);
        await serviceA.CreateAsync(new CreateMemberRequest
        {
            FullName = "في أ",
            MotherName = "الأم",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-22),
            MaritalStatusId = 1,
            Occupation = "موظف",
            RecruitmentNumber = "CA-1",
            RecruitmentCenterId = RecruitmentCenterId
        });

        var serviceB = MemberServiceTests.CreateService(
            db, CenterB, allowCreate: false, allowView: false, allowExport: true);
        await Assert.ThrowsAsync<MemberExportEmptyException>(() =>
            serviceB.ExportAsync(new MemberListQuery()));
    }

    [Fact]
    public async Task Export_denied_without_permission()
    {
        await using var db = MemberServiceTests.CreateDb();
        await MemberServiceTests.SeedReferencesAsync(db);
        var service = MemberServiceTests.CreateService(db, CenterA, allowCreate: false, allowView: false, allowExport: false);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() =>
            service.ExportAsync(new MemberListQuery()));
    }

    private static Dictionary<string, object?> ValidRow(string recruitment, string fullName = "منتسب استيراد")
        => new()
        {
            ["الاسم الكامل"] = fullName,
            ["اسم الأم"] = "الأم",
            ["تاريخ الميلاد"] = DateTime.UtcNow.AddYears(-25).ToString("yyyy-MM-dd"),
            ["الحالة الاجتماعية"] = 1,
            ["العمل/المهنة"] = "موظف",
            ["رقم المنتسب/التجنيد"] = recruitment,
            ["مركز التجنيد"] = RecruitmentCenterId.ToString(),
            ["زمرة الدم"] = 1,
            ["سكر الدم"] = null,
            ["الطول"] = 170,
            ["الوزن"] = 66,
            ["ضغط الدم"] = "120/80",
            ["النبض"] = 72,
            ["وشم"] = "لا",
            ["علامات مميزة"] = null
        };

    private static IFormFile BuildImportFile(params Dictionary<string, object?>[] rows)
    {
        using var wb = new XLWorkbook();
        var sheet = wb.Worksheets.Add("استيراد");
        for (var i = 0; i < MemberImportExcel.RequiredHeaders.Count; i++)
            sheet.Cell(1, i + 1).Value = MemberImportExcel.RequiredHeaders[i];

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < MemberImportExcel.RequiredHeaders.Count; c++)
            {
                var header = MemberImportExcel.RequiredHeaders[c];
                var value = rows[r].TryGetValue(header, out var v) ? v : null;
                if (value is null) continue;
                sheet.Cell(r + 2, c + 1).Value = value switch
                {
                    int i => i,
                    short s => s,
                    decimal d => d,
                    double dbl => dbl,
                    _ => value.ToString()
                };
            }
        }

        return ToFormFile(wb, "import.xlsx");
    }

    private static IFormFile ToFormFile(XLWorkbook workbook, string fileName)
    {
        var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
    }
}
