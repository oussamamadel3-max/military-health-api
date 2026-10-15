namespace Application.Common.Security;

public static class MustChangePasswordClaims
{
    public const string ClaimType = "must_change_password";
    public const string RequiredValue = "true";

    public const string RequiredMessage =
        "يجب تغيير كلمة المرور قبل استخدام النظام.";
}
