namespace Application.Options;

public sealed class LoginLockoutSettings
{
    public const string SectionName = "LoginLockout";

    public int MaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>سلم مدد الحظر بالثواني: 30 → 60 → 120 → 300 → 900.</summary>
    public int[] DurationsSeconds { get; set; } = [30, 60, 120, 300, 900];

    /// <summary>بعد هذه المدة منذ آخر LockoutEnd (والحساب غير محظور) يُصفَّر الـ tier.</summary>
    public int TierResetAfterSeconds { get; set; } = 86400;
}
