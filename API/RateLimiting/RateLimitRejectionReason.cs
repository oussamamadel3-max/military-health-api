namespace API.RateLimiting;

/// <summary>
/// Explicit cause of a rate-limit rejection. Callers must not infer this from the presence of Retry-After.
/// </summary>
public enum RateLimitRejectionReason
{
    Concurrency,
    SafetyCeiling,
    CategoryQuota,
    AuthWindow
}
