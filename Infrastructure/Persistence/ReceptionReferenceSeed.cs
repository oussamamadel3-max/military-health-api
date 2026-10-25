using Application.Common.ReferenceData;
using Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// يزرع قوائم الاستقبال الثابتة وقاعدة العمر على السياق الحالي.
/// إذا وُجد الرمز مسبقاً تُترك الصف كما هي، بما في ذلك التعطيل الإداري.
/// لا يزرع مراكز تجنيد: قائمتها بيانات تشغيلية تُدار من كتالوج recruitment-centers.
/// </summary>
public static class ReceptionReferenceSeed
{
    public static async Task ApplyAsync(
        AppDbContext context,
        string? updatedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var marital = await context.MaritalStatuses.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var item in ReceptionReferenceCatalog.MaritalStatuses)
        {
            if (ContainsCode(marital.Select(x => x.Code), item.Code))
                continue;

            context.MaritalStatuses.Add(new MaritalStatus
            {
                Code = item.Code,
                NameAr = item.NameAr,
                SortOrder = item.SortOrder,
                IsSystem = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var blood = await context.BloodTypes.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var item in ReceptionReferenceCatalog.BloodTypes)
        {
            if (ContainsCode(blood.Select(x => x.Code), item.Code))
                continue;

            context.BloodTypes.Add(new BloodType
            {
                Code = item.Code,
                NameAr = item.NameAr,
                SortOrder = item.SortOrder,
                IsSystem = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        var bmi = await context.BmiCategories.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var item in ReceptionReferenceCatalog.BmiCategories)
        {
            if (ContainsCode(bmi.Select(x => x.Code), item.Code))
                continue;

            context.BmiCategories.Add(new BmiCategory
            {
                Code = item.Code,
                NameAr = item.NameAr,
                MinBmi = item.MinBmi,
                MaxBmi = item.MaxBmi,
                SortOrder = item.SortOrder,
                IsActive = true
            });
        }

        var ageExists = await context.ValidationRules.AnyAsync(
            r => r.Code.ToLower() == ReceptionReferenceCatalog.AgeRuleCode.ToLower(),
            cancellationToken);
        if (!ageExists)
            context.ValidationRules.Add(CreateAgeRule(now, updatedByUserId));

        await context.SaveChangesAsync(cancellationToken);
    }

    public static ValidationRule CreateAgeRule(DateTimeOffset now, string? updatedByUserId = null) => new()
    {
        ValidationRuleId = Guid.NewGuid(),
        Code = ReceptionReferenceCatalog.AgeRuleCode,
        NameAr = ReceptionReferenceCatalog.AgeNameAr,
        TargetEntity = ReceptionReferenceCatalog.AgeTargetEntity,
        TargetField = ReceptionReferenceCatalog.AgeTargetField,
        MinValue = ReceptionReferenceCatalog.MinAgeYears,
        MaxValue = ReceptionReferenceCatalog.MaxAgeYears,
        Unit = ReceptionReferenceCatalog.AgeUnit,
        VersionNo = 1,
        ValidFrom = now,
        IsActive = true,
        UpdatedByUserId = updatedByUserId,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static bool ContainsCode(IEnumerable<string> codes, string code)
        => codes.Any(existing => string.Equals(existing, code, StringComparison.OrdinalIgnoreCase));
}
