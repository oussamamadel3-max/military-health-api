namespace Application.Services.Supervisor;

/// <summary>
/// قواعد اكتمال العيادات الإلزامية للقرار النهائي (BR-015 / BR-077).
/// قابلة لإعادة الاستخدام من بطاقات إصدار القرار وقائمة المتابعة.
/// </summary>
public static class MandatoryClinicCompleteness
{
    /// <summary>
    /// يعيد رموز العيادات الإلزامية التي لا يظهر لها تقييم في المجموعة المقدَّمة.
    /// </summary>
    public static IReadOnlyList<string> GetMissingClinicCodes(
        IReadOnlyList<(Guid ClinicId, string Code)> mandatoryClinics,
        IReadOnlySet<Guid> assessedClinicIds)
    {
        if (mandatoryClinics.Count == 0)
            return Array.Empty<string>();

        var missing = new List<string>(mandatoryClinics.Count);
        foreach (var clinic in mandatoryClinics)
        {
            if (!assessedClinicIds.Contains(clinic.ClinicId))
                missing.Add(clinic.Code);
        }

        return missing;
    }

    /// <summary>true عندما تغطي التقييمات كل العيادات الإلزامية.</summary>
    public static bool AreComplete(
        IReadOnlyList<(Guid ClinicId, string Code)> mandatoryClinics,
        IReadOnlySet<Guid> assessedClinicIds)
        => GetMissingClinicCodes(mandatoryClinics, assessedClinicIds).Count == 0;
}
