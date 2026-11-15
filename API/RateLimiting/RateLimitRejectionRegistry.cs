using System.Collections.Concurrent;

namespace API.RateLimiting;

/// <summary>
/// Process-local rejection counts keyed only by policy name and <see cref="RateLimitRejectionReason"/>.
/// The key set is bounded to known policies so the store cannot grow with user ids, IPs, or request data.
/// </summary>
internal sealed class RateLimitRejectionRegistry
{
    private static readonly HashSet<string> KnownPolicies = new(StringComparer.Ordinal)
    {
        RateLimitPolicies.Login,
        RateLimitPolicies.Refresh,
        RateLimitPolicies.Reports,
        RateLimitPolicies.HeavyRead,
        RateLimitPolicies.Administrative,
        RateLimitPolicies.ReferenceData,
        RateLimitPolicies.PublicApi,
        RateLimitPolicies.Safety,
        RateLimitPolicies.General
    };

    private static readonly HashSet<string> KnownReasons = new(StringComparer.Ordinal)
    {
        nameof(RateLimitRejectionReason.Concurrency),
        nameof(RateLimitRejectionReason.SafetyCeiling),
        nameof(RateLimitRejectionReason.CategoryQuota),
        nameof(RateLimitRejectionReason.AuthWindow)
    };

    private readonly ConcurrentDictionary<string, long> _counts = new(StringComparer.Ordinal);

    /// <summary>Increments the counter for a known policy and reason. Unknown labels collapse to fixed buckets.</summary>
    public void Increment(string policy, string reason)
    {
        if (!KnownPolicies.Contains(policy))
            policy = "unknown";
        if (!KnownReasons.Contains(reason))
            reason = "unspecified";

        var key = policy + "|" + reason;
        _counts.AddOrUpdate(key, 1, static (_, current) => current == long.MaxValue ? current : current + 1);
    }

    public long Count(string policy, RateLimitRejectionReason reason)
        => _counts.TryGetValue(policy + "|" + reason, out var count) ? count : 0;

    /// <summary>Stable, secret-free snapshot for logs. Format is policy|reason=count.</summary>
    public string FormatSnapshot()
    {
        if (_counts.IsEmpty)
            return string.Empty;

        return string.Join(
            "; ",
            _counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value));
    }
}
