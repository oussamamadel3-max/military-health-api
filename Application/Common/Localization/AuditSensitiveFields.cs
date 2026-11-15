namespace Application.Common.Localization;

/// <summary>
/// الحقول التي لا تدخل لقطة التدقيق ولا أي ملخص عرض. القائمة نفسها التي يستبعدها كاتب التدقيق.
/// </summary>
public static class AuditSensitiveFields
{
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreatedAt",
        "CreatedBy",
        "UpdatedAt",
        "UpdatedBy",
        "PasswordHash",
        "SecurityStamp",
        "ConcurrencyStamp",
        "NormalizedUserName",
        "NormalizedEmail",
        "LockoutEnd",
        "AccessFailedCount",
        "LoginLockoutTier",
        "LastLoginAt",
        "Token",
        "TokenHash",
        "RefreshToken",
        "Password",
        "OldValues",
        "NewValues"
    };

    public static bool IsSensitive(string? fieldName)
        => !string.IsNullOrWhiteSpace(fieldName) && All.Contains(fieldName);
}
