using Application.Common.ReferenceData;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

public class ExamClinicLegacyDiagnosticTests
{
    [Fact]
    public async Task Legacy_reserved_name_is_reported_and_left_unchanged()
    {
        await using var db = MemberServiceTests.CreateDb();
        var legacyId = Guid.NewGuid();
        db.Clinics.Add(new Clinic
        {
            ClinicId = legacyId,
            Code = "ORTHO",
            NameAr = "  العظمية  ",
            SortOrder = 9,
            IsActive = false,
            CountsForFinal = false
        });
        await db.SaveChangesAsync();

        var before = ExamClinicCatalog.Diagnose(
        [
            new ExamClinicSnapshot(legacyId, "ORTHO", "  العظمية  ", 9)
        ]);
        var finding = Assert.Single(before);
        Assert.Equal(legacyId, finding.ClinicId);
        Assert.Equal("ORTHO", finding.Code);
        Assert.Equal(ExamClinicCatalog.OrthopedicCode, finding.ExpectedCode);

        var seeded = await ExamClinicSeed.ApplyAsync(db);

        Assert.Contains(seeded.Diagnostics, item => item.ClinicId == legacyId && item.Code == "ORTHO");
        var legacy = await db.Clinics.SingleAsync(clinic => clinic.ClinicId == legacyId);
        Assert.Equal("ORTHO", legacy.Code);
        Assert.Equal("  العظمية  ", legacy.NameAr);
        Assert.Equal((short)9, legacy.SortOrder);
        Assert.False(legacy.IsActive);
        Assert.False(legacy.CountsForFinal);
        Assert.Equal(6, await db.Clinics.CountAsync());
    }
}
