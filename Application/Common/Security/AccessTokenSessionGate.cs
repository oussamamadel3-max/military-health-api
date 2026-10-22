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
