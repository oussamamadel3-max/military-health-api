using System.Threading.RateLimiting;
using API.RateLimiting;

namespace API.IntegrationTests.RateLimiting;

public class RateLimitChainTests
{
    [Fact]
    public void Concurrency_rejection_does_not_consume_the_following_token_bucket()
    {
        using var chain = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<string, string>(_ =>
                RateLimitPartition.Get("concurrency", _ => new ReasonedRateLimiter(
                    new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 1,
                        QueueLimit = 0
                    }),
                    RateLimitPolicies.HeavyRead,
                    RateLimitRejectionReason.Concurrency))),
            PartitionedRateLimiter.Create<string, string>(_ =>
                RateLimitPartition.Get("tokens", _ => new ReasonedRateLimiter(
                    new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 2,
                        TokensPerPeriod = 1,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                        AutoReplenishment = false
                    }),
                    RateLimitPolicies.Safety,
                    RateLimitRejectionReason.SafetyCeiling))));

        using var held = chain.AttemptAcquire("user-1");
        Assert.True(held.IsAcquired);

        using var rejected = chain.AttemptAcquire("user-1");
        Assert.False(rejected.IsAcquired);
        Assert.Equal(nameof(RateLimitRejectionReason.Concurrency), Reason(rejected));
        Assert.Equal(RateLimitPolicies.HeavyRead, Policy(rejected));
        Assert.False(rejected.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan _));

        held.Dispose();

        using var after = chain.AttemptAcquire("user-1");
        Assert.True(after.IsAcquired);
    }

    [Fact]
    public void Sequential_limiter_releases_the_concurrency_lease_when_the_token_bucket_rejects()
    {
        var concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0
        });
        var tokens = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            AutoReplenishment = false
        });
        using var chain = new SequentialRateLimiter(concurrency, tokens);

        using var first = chain.AttemptAcquire(1);
        Assert.True(first.IsAcquired);
        first.Dispose();

        using var rejected = chain.AttemptAcquire(1);
        Assert.False(rejected.IsAcquired);
        Assert.True(rejected.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);

        using var concurrencyAfter = concurrency.AttemptAcquire(1);
        Assert.True(concurrencyAfter.IsAcquired);
    }

    [Fact]
    public void Sliding_window_rejection_exposes_retry_after_when_the_limiter_calculates_it()
    {
        using var window = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromSeconds(60),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true
        });

        using var first = window.AttemptAcquire(1);
        Assert.True(first.IsAcquired);
        using var rejected = window.AttemptAcquire(1);
        Assert.False(rejected.IsAcquired);
        // .NET 10 SlidingWindowRateLimiter does not attach RetryAfter. The HTTP layer must not invent one.
        Assert.False(rejected.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan _));
    }

    [Fact]
    public void Rejection_registry_counts_only_policy_and_reason()
    {
        var registry = new RateLimitRejectionRegistry();
        registry.Increment(RateLimitPolicies.HeavyRead, nameof(RateLimitRejectionReason.CategoryQuota));
        registry.Increment("user:secret-id", "Concurrency");

        Assert.Equal(1, registry.Count(RateLimitPolicies.HeavyRead, RateLimitRejectionReason.CategoryQuota));
        Assert.Equal(0, registry.Count("user:secret-id", RateLimitRejectionReason.Concurrency));
        var snapshot = registry.FormatSnapshot();
        Assert.Contains("heavy-read|CategoryQuota=1", snapshot, StringComparison.Ordinal);
        Assert.Contains("unknown|Concurrency=1", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-id", snapshot, StringComparison.Ordinal);
    }

    private static string Reason(RateLimitLease lease)
    {
        Assert.True(lease.TryGetMetadata(RateLimitMetadataNames.Reason, out object? reason));
        return Assert.IsType<string>(reason);
    }

    private static string Policy(RateLimitLease lease)
    {
        Assert.True(lease.TryGetMetadata(RateLimitMetadataNames.Policy, out object? policy));
        return Assert.IsType<string>(policy);
    }
}
