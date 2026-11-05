namespace API.RateLimiting;

public static class RateLimitPolicies
{
    public const string Login = "login";

    /// <summary>MH-SEC-055 / D3: dedicated sliding-window limit for Auth Refresh (IP partition).</summary>
    public const string Refresh = "refresh";

    public const string Reports = "reports";
    public const string PublicApi = "public-api";
}
