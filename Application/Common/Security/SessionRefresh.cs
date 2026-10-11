namespace Application.Common.Security;

public static class SessionRefresh
{
    public const string RejectedMessage = "رمز التحديث غير صالح أو منتهي الصلاحية";

    public const string Missing = "missing";
    public const string Invalid = "invalid";
    public const string Expired = "expired";
    public const string Reused = "reused";
    public const string Inactive = "inactive";
    public const string SecurityStamp = "security_stamp";

    public const string LogoutReason = "logout";
    public const string RotatedReason = "rotated";
    public const string ReuseReason = "reuse";

    public static string ReasonText(string reasonCode) => reasonCode switch
    {
        Missing => "رفض تجديد الجلسة لغياب رمز التحديث",
        Expired => "رفض تجديد الجلسة لانتهاء رمز التحديث",
        Reused => "رفض تجديد الجلسة لأن رمز التحديث مستخدم أو ملغى",
        SecurityStamp => "رفض تجديد الجلسة بسبب تغيّر حالة أمان الحساب",
        Inactive => "رفض تجديد الجلسة لحساب غير فعّال",
        _ => "رفض تجديد الجلسة"
    };
}
