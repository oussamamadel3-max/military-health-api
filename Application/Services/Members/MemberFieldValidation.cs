using System.Globalization;
using System.Text.RegularExpressions;
using Application.Dtos.Members;
using Application.Exceptions;
using Domain.Entities.Core;

namespace Application.Services.Members;

/// <summary>
/// قواعد تحقق مشتركة لإنشاء/تعديل المنتسب (قابلة لإعادة الاستخدام لاحقاً للاستيراد FR-028).
/// لا تثبّت حدود قياسات جديدة (CON-11)؛ تطبق ValidationRule النشطة إن وُجدت فقط.
/// </summary>
public static class MemberFieldValidation
{
    private static readonly Regex BloodPressurePattern = new(
        @"^\d{2,3}/\d{2,3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>الحد الأدنى والأعلى التقريبي للعمر وفق السلوك المؤقت BR-004.</summary>
    public const int MinAgeYears = 18;
    public const int MaxAgeYears = 120;

    public static void ValidateCreateOrUpdate(
        string fullName,
        string motherName,
        DateOnly birthDate,
        short maritalStatusId,
        string occupation,
        string recruitmentNumber,
        Guid recruitmentCenterId,
        short? bloodTypeId,
        decimal? bloodSugarMgDl,
        decimal? heightCm,
        decimal? weightKg,
        string? bloodPressure,
        short? pulseBpm,
        DateOnly today,
        IReadOnlyList<ValidationRule> activeRules)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new InvalidMemberDataException("الاسم الكامل مطلوب.");
        if (string.IsNullOrWhiteSpace(motherName))
            throw new InvalidMemberDataException("اسم الأم مطلوب.");
        if (string.IsNullOrWhiteSpace(occupation))
            throw new InvalidMemberDataException("العمل/المهنة مطلوب.");
        if (string.IsNullOrWhiteSpace(recruitmentNumber))
            throw new InvalidMemberDataException("رقم المنتسب/التجنيد مطلوب.");
        if (maritalStatusId <= 0)
            throw new InvalidMemberDataException("الحالة الاجتماعية مطلوبة.");
        if (recruitmentCenterId == Guid.Empty)
            throw new InvalidMemberDataException("مركز التجنيد مطلوب.");

        ValidateBirthDate(birthDate, today);
        ValidateOptionalMeasurements(bloodSugarMgDl, heightCm, weightKg, bloodPressure, pulseBpm, activeRules);
        _ = bloodTypeId;
    }

    public static void ValidateCreateOrUpdate(CreateMemberRequest request, DateOnly today, IReadOnlyList<ValidationRule> activeRules)
        => ValidateCreateOrUpdate(
            request.FullName,
            request.MotherName,
            request.BirthDate,
            request.MaritalStatusId,
            request.Occupation,
            request.RecruitmentNumber,
            request.RecruitmentCenterId,
            request.BloodTypeId,
            request.BloodSugarMgDl,
            request.HeightCm,
            request.WeightKg,
            request.BloodPressure,
            request.PulseBpm,
            today,
            activeRules);

    public static void ValidateCreateOrUpdate(UpdateMemberRequest request, DateOnly today, IReadOnlyList<ValidationRule> activeRules)
        => ValidateCreateOrUpdate(
            request.FullName,
            request.MotherName,
            request.BirthDate,
            request.MaritalStatusId,
            request.Occupation,
            request.RecruitmentNumber,
            request.RecruitmentCenterId,
            request.BloodTypeId,
            request.BloodSugarMgDl,
            request.HeightCm,
            request.WeightKg,
            request.BloodPressure,
            request.PulseBpm,
            today,
            activeRules);

    public static void ValidateBirthDate(DateOnly birthDate, DateOnly today)
    {
        if (birthDate > today)
            throw new InvalidMemberDataException("تاريخ الميلاد لا يمكن أن يكون في المستقبل.");

        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age))
            age--;

        if (age < MinAgeYears || age > MaxAgeYears)
            throw new InvalidMemberDataException(
                $"تاريخ الميلاد يجب أن يقابل عمراً بين {MinAgeYears} و{MaxAgeYears} سنة.");
    }

    public static void ValidateListDateFilter(DateOnly? date, DateOnly? fromDate, DateOnly? toDate)
    {
        if (date.HasValue && (fromDate.HasValue || toDate.HasValue))
            throw new InvalidMemberDataException("استخدم تاريخاً محدداً أو مجالاً زمنياً، وليس الاثنين معاً.");

        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            throw new InvalidMemberDataException("تاريخ البداية يجب ألا يتجاوز تاريخ النهاية.");
    }

    public static void ValidateOptionalMeasurements(
        decimal? bloodSugarMgDl,
        decimal? heightCm,
        decimal? weightKg,
        string? bloodPressure,
        short? pulseBpm,
        IReadOnlyList<ValidationRule> activeRules)
    {
        if (bloodSugarMgDl is <= 0)
            throw new InvalidMemberDataException("نسبة سكر الدم يجب أن تكون أكبر من صفر.");
        if (heightCm is <= 0)
            throw new InvalidMemberDataException("الطول يجب أن يكون أكبر من صفر.");
        if (weightKg is <= 0)
            throw new InvalidMemberDataException("الوزن يجب أن يكون أكبر من صفر.");
        if (pulseBpm is <= 0)
            throw new InvalidMemberDataException("النبض يجب أن يكون أكبر من صفر.");

        if (!string.IsNullOrWhiteSpace(bloodPressure))
        {
            var trimmed = bloodPressure.Trim();
            if (trimmed.Length > 15)
                throw new InvalidMemberDataException("صيغة ضغط الدم تتجاوز الحد المسموح.");
            if (!BloodPressurePattern.IsMatch(trimmed))
                throw new InvalidMemberDataException("صيغة ضغط الدم غير صحيحة. استخدم الشكل مثل 120/80.");
        }

        ApplyActiveRules(nameof(CreateMemberRequest.BloodSugarMgDl), bloodSugarMgDl, activeRules, "نسبة سكر الدم");
        ApplyActiveRules(nameof(CreateMemberRequest.HeightCm), heightCm, activeRules, "الطول");
        ApplyActiveRules(nameof(CreateMemberRequest.WeightKg), weightKg, activeRules, "الوزن");
        if (pulseBpm.HasValue)
            ApplyActiveRules(nameof(CreateMemberRequest.PulseBpm), pulseBpm.Value, activeRules, "النبض");
    }

    private static void ApplyActiveRules(
        string fieldName,
        decimal? value,
        IReadOnlyList<ValidationRule> activeRules,
        string fieldLabelAr)
    {
        if (value is null)
            return;

        foreach (var rule in activeRules.Where(r =>
                     string.Equals(r.TargetField, fieldName, StringComparison.OrdinalIgnoreCase)))
        {
            if (rule.MinValue.HasValue && value.Value < rule.MinValue.Value)
                throw new InvalidMemberDataException(
                    $"{fieldLabelAr} أقل من الحد الأدنى المعتمد ({Format(rule.MinValue.Value)}).");
            if (rule.MaxValue.HasValue && value.Value > rule.MaxValue.Value)
                throw new InvalidMemberDataException(
                    $"{fieldLabelAr} أكبر من الحد الأعلى المعتمد ({Format(rule.MaxValue.Value)}).");
        }
    }

    private static string Format(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
