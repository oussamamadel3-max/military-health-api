namespace Application.Common.Security;

/// <summary>
/// أنواع أحداث إدارية صريحة على حساب مستخدم، تُكمّل تدقيق EF التلقائي دون كشف أسرار.
/// </summary>
public static class UserAccountAdminEventTypes
{
    public const string PasswordReset = "UserPasswordReset";
    public const string SessionsInvalidated = "UserSessionsInvalidated";
    public const string AccountLocked = "UserAccountLocked";
    public const string AccountUnlocked = "UserAccountUnlocked";
}
