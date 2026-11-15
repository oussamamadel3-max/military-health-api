namespace API.RateLimiting;

public static class RateLimitPolicies
{
    public const string Login = "login";

    /// <summary>MH-SEC-055 / D3: dedicated sliding-window limit for Auth Refresh (IP partition).</summary>
    public const string Refresh = "refresh";

    public const string Reports = "reports";

    /// <summary>Interactive operational-report reads. Export stays on <see cref="Reports"/>.</summary>
    public const string HeavyRead = "heavy-read";

    /// <summary>Field-permission reads. Account, session, and privilege mutations stay on <see cref="General"/>.</summary>
    public const string Administrative = "administrative";

    /// <summary>Repeated lookups: centers list, assigned centers, and reference-data reads.</summary>
    public const string ReferenceData = "reference-data";

    public const string PublicApi = "public-api";

    /// <summary>Shared ceiling applied to every limited request. Not an endpoint policy name.</summary>
    public const string Safety = "safety";

    /// <summary>Bucket for endpoints that do not opt into a dedicated policy.</summary>
    public const string General = "general";
}
