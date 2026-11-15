using System.Threading.RateLimiting;

namespace API.RateLimiting;

/// <summary>
/// Delegates acquisition to an inner limiter and, on rejection only, tags the lease with the policy and an explicit reason.
/// Successful leases are returned unchanged so token and concurrency accounting stay with the inner limiter.
/// </summary>
internal sealed class ReasonedRateLimiter : RateLimiter
{
    private readonly RateLimiter _inner;
    private readonly string _policy;
    private readonly string _reason;
    private int _disposed;

    public ReasonedRateLimiter(RateLimiter inner, string policy, RateLimitRejectionReason reason)
    {
        _inner = inner;
        _policy = policy;
        _reason = reason.ToString();
    }

    public override TimeSpan? IdleDuration => _inner.IdleDuration;

    public override RateLimiterStatistics? GetStatistics() => _inner.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var lease = _inner.AttemptAcquire(permitCount);
        return lease.IsAcquired ? lease : new ReasonedLease(lease, _policy, _reason);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var lease = await _inner.AcquireAsync(permitCount, cancellationToken).ConfigureAwait(false);
        return lease.IsAcquired ? lease : new ReasonedLease(lease, _policy, _reason);
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (disposing)
            _inner.Dispose();
    }

    protected override ValueTask DisposeAsyncCore()
    {
        Dispose(true);
        return default;
    }

    private sealed class ReasonedLease : RateLimitLease
    {
        private readonly RateLimitLease _inner;
        private readonly string _policy;
        private readonly string _reason;
        private int _disposed;

        public ReasonedLease(RateLimitLease inner, string policy, string reason)
        {
            _inner = inner;
            _policy = policy;
            _reason = reason;
        }

        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames
        {
            get
            {
                yield return RateLimitMetadataNames.Policy;
                yield return RateLimitMetadataNames.Reason;
                foreach (var name in _inner.MetadataNames)
                    yield return name;
            }
        }

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == RateLimitMetadataNames.Policy)
            {
                metadata = _policy;
                return true;
            }

            if (metadataName == RateLimitMetadataNames.Reason)
            {
                metadata = _reason;
                return true;
            }

            return _inner.TryGetMetadata(metadataName, out metadata);
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (disposing)
                _inner.Dispose();
        }
    }
}
