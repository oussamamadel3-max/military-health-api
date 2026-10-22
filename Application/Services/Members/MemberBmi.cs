using Domain.Entities.Core;

namespace Application.Services.Members;

/// <summary>
/// حساب وتصنيف مؤشر كتلة الجسم للعرض (FR-023). القيمة المخزّنة في Member.Bmi عمود computed في قاعدة البيانات.
/// </summary>
public static class MemberBmi
{
    /// <summary>يحسب BMI مقرّباً لمنزلة عشرية واحدة، أو null عند نقص الطول أو الوزن أو طول غير موجب.</summary>
    public static decimal? Calculate(decimal? heightCm, decimal? weightKg)
    {
        if (heightCm is null || weightKg is null)
            return null;
        if (heightCm <= 0 || weightKg <= 0)
            return null;

        var meters = heightCm.Value / 100m;
        var raw = weightKg.Value / (meters * meters);
        return Math.Round(raw, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>يصنّف BMI وفق فئات مرجعية نشطة دون اختراع حدود جديدة.</summary>
    public static string? Classify(decimal? bmi, IEnumerable<BmiCategory> categories)
    {
        if (bmi is null)
            return null;

        var match = categories
            .Where(c => c.IsActive
                && bmi.Value >= c.MinBmi
                && (c.MaxBmi is null || bmi.Value < c.MaxBmi))
            .OrderBy(c => c.SortOrder)
            .FirstOrDefault();

        return match?.NameAr;
    }
}
