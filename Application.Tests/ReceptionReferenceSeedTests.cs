using Application.Common.ReferenceData;
using Application.Services.Members;
using Domain.Entities.Core;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

public class ReceptionReferenceSeedTests
{
    [Fact]
    public async Task Apply_twice_does_not_duplicate_and_keeps_disabled_rows()
    {
        await using var db = MemberServiceTests.CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.MaritalStatuses.Add(new MaritalStatus
        {
            Code = "SINGLE",
            NameAr = "أعزب",
            SortOrder = 1,
            IsSystem = true,
            IsActive = false,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BloodTypes.Add(new BloodType
        {
            Code = "O+",
            NameAr = "O+",
            SortOrder = 7,
            IsSystem = true,
            IsActive = false,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.BmiCategories.Add(new BmiCategory
        {
            Code = "NORMAL",
            NameAr = "طبيعي",
            MinBmi = 18.5m,
            MaxBmi = 25m,
            SortOrder = 2,
            IsActive = false
        });
        db.ValidationRules.Add(new ValidationRule
        {
            ValidationRuleId = Guid.NewGuid(),
            Code = ReceptionReferenceCatalog.AgeRuleCode,
            NameAr = "عمر معطّل",
            TargetEntity = ReceptionReferenceCatalog.AgeTargetEntity,
            TargetField = ReceptionReferenceCatalog.AgeTargetField,
            MinValue = 21,
            MaxValue = 40,
            Unit = ReceptionReferenceCatalog.AgeUnit,
            VersionNo = 1,
            ValidFrom = now,
            IsActive = false,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        await ReceptionReferenceSeed.ApplyAsync(db);
        await ReceptionReferenceSeed.ApplyAsync(db);

        Assert.Equal(ReceptionReferenceCatalog.MaritalStatuses.Count, await db.MaritalStatuses.CountAsync());
        Assert.Equal(ReceptionReferenceCatalog.BloodTypes.Count, await db.BloodTypes.CountAsync());
        Assert.Equal(ReceptionReferenceCatalog.BmiCategories.Count, await db.BmiCategories.CountAsync());
        Assert.Equal(1, await db.ValidationRules.CountAsync());
        Assert.Equal(0, await db.RecruitmentCenters.CountAsync());

        Assert.False(await db.MaritalStatuses.Where(x => x.Code == "SINGLE").Select(x => x.IsActive).SingleAsync());
        Assert.False(await db.BloodTypes.Where(x => x.Code == "O+").Select(x => x.IsActive).SingleAsync());
        Assert.False(await db.BmiCategories.Where(x => x.Code == "NORMAL").Select(x => x.IsActive).SingleAsync());
        var age = await db.ValidationRules.SingleAsync();
        Assert.False(age.IsActive);
        Assert.Equal(21m, age.MinValue);
        Assert.Equal(40m, age.MaxValue);
    }

    [Fact]
    public async Task Apply_on_empty_database_seeds_reception_lists_and_age_rule()
    {
        await using var db = MemberServiceTests.CreateDb();

        await ReceptionReferenceSeed.ApplyAsync(db);

        Assert.Equal(4, await db.MaritalStatuses.CountAsync(x => x.IsActive && x.IsSystem));
        Assert.Equal(8, await db.BloodTypes.CountAsync(x => x.IsActive && x.IsSystem));
        Assert.Equal(4, await db.BmiCategories.CountAsync(x => x.IsActive));
        Assert.Equal(0, await db.RecruitmentCenters.CountAsync());

        var age = await db.ValidationRules.SingleAsync();
        Assert.Equal(ReceptionReferenceCatalog.AgeRuleCode, age.Code);
        Assert.Equal(ReceptionReferenceCatalog.AgeTargetField, age.TargetField);
        Assert.Equal(ReceptionReferenceCatalog.MinAgeYears, age.MinValue);
        Assert.Equal(ReceptionReferenceCatalog.MaxAgeYears, age.MaxValue);
        Assert.True(age.IsActive);

        var categories = await db.BmiCategories.Where(x => x.IsActive).ToListAsync();
        Assert.Equal("نقص وزن", MemberBmi.Classify(18.4m, categories));
        Assert.Equal("طبيعي", MemberBmi.Classify(18.5m, categories));
        Assert.Equal("طبيعي", MemberBmi.Classify(24.9m, categories));
        Assert.Equal("زيادة وزن", MemberBmi.Classify(25.0m, categories));
        Assert.Equal("سمنة", MemberBmi.Classify(30.0m, categories));
    }

    [Fact]
    public void Age_bounds_follow_the_active_rule_only()
    {
        var today = new DateOnly(2026, 10, 25);
        var rules = new[] { ReceptionReferenceSeed.CreateAgeRule(DateTimeOffset.UtcNow) };

        MemberFieldValidation.ValidateBirthDate(new DateOnly(2008, 10, 25), today, rules);
        MemberFieldValidation.ValidateBirthDate(new DateOnly(1906, 10, 25), today, rules);

        var young = Assert.Throws<Application.Exceptions.InvalidMemberDataException>(() =>
            MemberFieldValidation.ValidateBirthDate(new DateOnly(2008, 10, 26), today, rules));
        var old = Assert.Throws<Application.Exceptions.InvalidMemberDataException>(() =>
            MemberFieldValidation.ValidateBirthDate(new DateOnly(1905, 10, 25), today, rules));
        Assert.Contains("الحد الأدنى", young.Message);
        Assert.Contains("الحد الأعلى", old.Message);

        MemberFieldValidation.ValidateBirthDate(new DateOnly(2016, 10, 25), today, []);
    }
}
