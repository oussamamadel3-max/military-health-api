namespace Application.Common.ReferenceData;

/// <summary>
/// القيم الثابتة اللازمة لتشغيل الاستقبال. تُزرع مرة واحدة من Seed القالب.
/// مراكز التجنيد ليست هنا: قائمتها بيانات تشغيلية تُدخل من كتالوج recruitment-centers
/// بصلاحية System.ReferenceData، ولا تُخترع في البذر.
/// </summary>
public static class ReceptionReferenceCatalog
{
    public const string AgeRuleCode = "AGE";
    public const string AgeTargetEntity = "Member";
    public const string AgeTargetField = "BirthDate";
    public const string AgeNameAr = "العمر";
    public const string AgeUnit = "سنة";

    /// <summary>الحد الأدنى المعتمد للعمر بالسنوات (قاعدة AGE).</summary>
    public const decimal MinAgeYears = 18;

    /// <summary>الحد الأعلى المعتمد للعمر بالسنوات (قاعدة AGE).</summary>
    public const decimal MaxAgeYears = 120;

    public static readonly IReadOnlyList<CodedLookupSeed> MaritalStatuses =
    [
        new("SINGLE", "أعزب", 1),
        new("MARRIED", "متزوج", 2),
        new("DIVORCED", "مطلق", 3),
        new("WIDOWED", "أرمل", 4)
    ];

    public static readonly IReadOnlyList<CodedLookupSeed> BloodTypes =
    [
        new("A+", "A+", 1),
        new("A-", "A-", 2),
        new("B+", "B+", 3),
        new("B-", "B-", 4),
        new("AB+", "AB+", 5),
        new("AB-", "AB-", 6),
        new("O+", "O+", 7),
        new("O-", "O-", 8)
    ];

    /// <summary>
    /// فئات منظمة الصحة العالمية. الحد الأدنى شامل والأعلى حصري، كما يحسب MemberBmi.Classify.
    /// الفئة الأخيرة بلا حد أعلى حتى يُصنَّف كل مؤشر محسوب.
    /// </summary>
    public static readonly IReadOnlyList<BmiCategorySeed> BmiCategories =
    [
        new("UNDER", "نقص وزن", 0m, 18.5m, 1),
        new("NORMAL", "طبيعي", 18.5m, 25m, 2),
        new("OVER", "زيادة وزن", 25m, 30m, 3),
        new("OBESE", "سمنة", 30m, null, 4)
    ];

    public readonly record struct CodedLookupSeed(string Code, string NameAr, short SortOrder);

    public readonly record struct BmiCategorySeed(
        string Code,
        string NameAr,
        decimal MinBmi,
        decimal? MaxBmi,
        short SortOrder);
}
