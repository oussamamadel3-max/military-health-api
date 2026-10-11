namespace Application.Common.Security;

public static class AccessTokenSessionGate
{
    /// <summary>
    /// نفس شرط رفض access token المستخدم في JwtExtensions.OnTokenValidated.
    /// </summary>
    public static bool IsAccessTokenAccepted(bool userExists, bool isActive, string? tokenSecurityStamp, string? userSecurityStamp)
    {
        if (!userExists || !isActive)
            return false;

        if (string.IsNullOrEmpty(tokenSecurityStamp) ||
            string.IsNullOrEmpty(userSecurityStamp) ||
            !string.Equals(tokenSecurityStamp, userSecurityStamp, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}

public static class SessionFamilyGate
{
    public const string ClaimType = "session_family";

    public static bool AllowsAccess(string? sessionFamily, bool familyHasActiveRefreshToken)
    {
        if (!Guid.TryParse(sessionFamily, out _))
            return false;

        return familyHasActiveRefreshToken;
    }
}
