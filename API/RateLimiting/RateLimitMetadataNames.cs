namespace API.RateLimiting;

/// <summary>
/// Lease metadata written by <see cref="ReasonedRateLimiter"/>. Values are policy names and reason labels, never user identifiers.
/// </summary>
internal static class RateLimitMetadataNames
{
    public const string Policy = "MilitaryHealth.RateLimit.Policy";
    public const string Reason = "MilitaryHealth.RateLimit.Reason";
}
