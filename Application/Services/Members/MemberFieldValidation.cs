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
        var errors = CollectCreateOrUpdateErrors(
            fullName,
            motherName,
            birthDate,
            maritalStatusId,
            occupation,
            recruitmentNumber,
            recruitmentCenterId,
            bloodTypeId,
            bloodSugarMgDl,
            heightCm,
            weightKg,
            bloodPressure,
            pulseBpm,
            today,
            activeRules);
        if (errors.Count > 0)
            throw new InvalidMemberDataException(errors[0]);
    }

    /// <summary>
    /// يجمع كل أخطاء التحقق للصف دون التوقف عند أول خطأ (مفيد لمعاينة الاستيراد).
    /// </summary>
    public static IReadOnlyList<string> CollectCreateOrUpdateErrors(
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
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(fullName))
            errors.Add("الاسم الكامل مطلوب.");
        if (string.IsNullOrWhiteSpace(motherName))
            errors.Add("اسم الأم مطلوب.");
        if (string.IsNullOrWhiteSpace(occupation))
            errors.Add("العمل/المهنة مطلوب.");
        if (string.IsNullOrWhiteSpace(recruitmentNumber))
            errors.Add("رقم المنتسب/التجنيد مطلوب.");
        if (maritalStatusId <= 0)
            errors.Add("الحالة الاجتماعية مطلوبة.");
        if (recruitmentCenterId == Guid.Empty)
            errors.Add("مركز التجنيد مطلوب.");

        try
        {
            ValidateBirthDate(birthDate, today);
        }
        catch (InvalidMemberDataException ex)
        {
            errors.Add(ex.Message);
        }

        CollectOptionalMeasurementErrors(
            bloodSugarMgDl, heightCm, weightKg, bloodPressure, pulseBpm, activeRules, errors);

        _ = bloodTypeId;
        return errors;
    }

    private static void CollectOptionalMeasurementErrors(
        decimal? bloodSugarMgDl,
        decimal? heightCm,
        decimal? weightKg,
        string? bloodPressure,
        short? pulseBpm,
        IReadOnlyList<ValidationRule> activeRules,
        List<string> errors)
    {
        if (bloodSugarMgDl is <= 0)
            errors.Add("نسبة سكر الدم يجب أن تكون أكبر من صفر.");
        if (heightCm is <= 0)
            errors.Add("الطول يجب أن يكون أكبر من صفر.");
        if (weightKg is <= 0)
            errors.Add("الوزن يجب أن يكون أكبر من صفر.");
        if (pulseBpm is <= 0)
            errors.Add("النبض يجب أن يكون أكبر من صفر.");

        if (!string.IsNullOrWhiteSpace(bloodPressure))
        {
            var trimmed = bloodPressure.Trim();
            if (trimmed.Length > 15)
                errors.Add("صيغة ضغط الدم تتجاوز الحد المسموح.");
            else if (!BloodPressurePattern.IsMatch(trimmed))
                errors.Add("صيغة ضغط الدم غير صحيحة. استخدم الشكل مثل 120/80.");
        }

        CollectActiveRuleErrors(nameof(CreateMemberRequest.BloodSugarMgDl), bloodSugarMgDl, activeRules, "نسبة سكر الدم", errors);
        CollectActiveRuleErrors(nameof(CreateMemberRequest.HeightCm), heightCm, activeRules, "الطول", errors);
        CollectActiveRuleErrors(nameof(CreateMemberRequest.WeightKg), weightKg, activeRules, "الوزن", errors);
        if (pulseBpm.HasValue)
            CollectActiveRuleErrors(nameof(CreateMemberRequest.PulseBpm), pulseBpm.Value, activeRules, "النبض", errors);
    }

    private static void CollectActiveRuleErrors(
        string fieldName,
        decimal? value,
        IReadOnlyList<ValidationRule> activeRules,
        string fieldLabelAr,
        List<string> errors)
    {
        if (value is null)
            return;

        foreach (var rule in activeRules.Where(r =>
                     string.Equals(r.TargetField, fieldName, StringComparison.OrdinalIgnoreCase)))
        {
            if (rule.MinValue.HasValue && value.Value < rule.MinValue.Value)
                errors.Add($"{fieldLabelAr} أقل من الحد الأدنى المعتمد ({Format(rule.MinValue.Value)}).");
            if (rule.MaxValue.HasValue && value.Value > rule.MaxValue.Value)
                errors.Add($"{fieldLabelAr} أكبر من الحد الأعلى المعتمد ({Format(rule.MaxValue.Value)}).");
        }
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

    /// <summary>
    /// يفسّر قيمة الباركود كرقم ملف ويضبط <see cref="MemberListQuery.FileNumber"/>.
    /// عند فشل التحليل يتيح البحث اليدوي برسالة واضحة دون إنشاء ملف.
    /// </summary>
    public static void ApplyBarcodeToFileNumber(MemberListQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Barcode))
            return;

        var raw = query.Barcode.Trim();
        if (!long.TryParse(raw, out var fileNumber) || fileNumber <= 0)
            throw new InvalidMemberDataException(
                "قيمة الباركود غير صالحة أو تالفة. أدخل رقم الملف يدوياً أو امسح رمزاً صحيحاً.");

        if (query.FileNumber.HasValue && query.FileNumber.Value != fileNumber)
            throw new InvalidMemberDataException(
                "تعارض بين رقم الملف وقيمة الباركود. استخدم معياراً واحداً للبحث.");

        query.FileNumber = fileNumber;
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
