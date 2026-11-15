using Application.Common.ReferenceData;
using Application.Exceptions;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Application.Tests;

public class ExamClinicSeedTests
{
    [Fact]
    public async Task Apply_on_empty_database_inserts_five_active_final_clinics_and_second_run_keeps_settings()
    {
        await using var db = MemberServiceTests.CreateDb();

        var first = await ExamClinicSeed.ApplyAsync(db);
        Assert.Empty(first.Diagnostics);

        var orthopedic = await db.Clinics.SingleAsync(clinic => clinic.Code == ExamClinicCatalog.OrthopedicCode);
        orthopedic.IsActive = false;
        orthopedic.CountsForFinal = false;
        orthopedic.NameAr = "اسم إداري";
        await db.SaveChangesAsync();

        var second = await ExamClinicSeed.ApplyAsync(db);

        Assert.Empty(second.Diagnostics);
        Assert.Equal(5, await db.Clinics.CountAsync());
        var kept = await db.Clinics.SingleAsync(clinic => clinic.Code == ExamClinicCatalog.OrthopedicCode);
        Assert.Equal(orthopedic.ClinicId, kept.ClinicId);
        Assert.False(kept.IsActive);
        Assert.False(kept.CountsForFinal);
        Assert.Equal("اسم إداري", kept.NameAr);
        Assert.Equal((short)5, kept.SortOrder);
    }

    [Fact]
    public async Task Apply_rejects_sort_conflict_without_inserting_any_canonical_clinic()
    {
        await using var db = MemberServiceTests.CreateDb();
        var legacyId = Guid.NewGuid();
        db.Clinics.Add(new Clinic
        {
            ClinicId = legacyId,
            Code = "ORTHO",
            NameAr = "العظمية",
            SortOrder = 5,
            IsActive = true,
            CountsForFinal = false
        });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ExamClinicSeedConflictException>(() => ExamClinicSeed.ApplyAsync(db));

        Assert.Contains("ORTHOPEDIC", error.Message, StringComparison.Ordinal);
        Assert.Contains("الترتيب 5", error.Message, StringComparison.Ordinal);
        Assert.Contains(legacyId.ToString("D"), error.Message, StringComparison.Ordinal);
        Assert.Contains("ORTHO", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, await db.Clinics.CountAsync());
        var legacy = await db.Clinics.SingleAsync();
        Assert.Equal("ORTHO", legacy.Code);
        Assert.Equal("العظمية", legacy.NameAr);
        Assert.Equal((short)5, legacy.SortOrder);
        Assert.False(legacy.CountsForFinal);
    }

    [Fact]
    public void Unique_index_violation_is_described_without_a_raw_database_exception()
    {
        var postgres = new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            "23505");
        var update = new DbUpdateException("save failed", postgres);

        Assert.True(ExamClinicSeed.IsUniqueViolation(update));
        var description = ExamClinicSeed.DescribeUniqueViolation(update);
        Assert.Contains("23505", description, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicate key", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shared_lock_key_is_stable()
    {
        Assert.Equal(720_240, ExamClinicSeed.SharedLockClass);
        Assert.Equal(1, ExamClinicSeed.SharedLockKey);
    }
}

public class ExamClinicNameNormalizationTests
{
    [Fact]
    public void Normalize_collapses_spaces_diacritics_and_alef_forms()
    {
        Assert.Equal("الاذنية", ExamClinicCatalog.NormalizeName("  الإِذْنِيَّة  "));
        Assert.Equal("العيادة العظمية", ExamClinicCatalog.NormalizeName("العيادة\u00A0  العظمية"));
        Assert.Equal("متابعة العظمية", ExamClinicCatalog.NormalizeName("متابعة العظمية"));
        ExamClinicCatalog.EnsureAssignable("CARDIOLOGY", "متابعة العظمية");
    }
}
