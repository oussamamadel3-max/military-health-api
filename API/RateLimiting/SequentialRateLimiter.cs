using System.Threading.RateLimiting;

namespace API.RateLimiting;

/// <summary>
/// Acquires from <paramref name="first"/> then <paramref name="second"/>.
/// If the second limiter rejects, the first lease is released (important for Concurrency → TokenBucket).
/// </summary>
internal sealed class SequentialRateLimiter : RateLimiter
{
    private readonly RateLimiter _first;
    private readonly RateLimiter _second;
    private int _disposed;

    public SequentialRateLimiter(RateLimiter first, RateLimiter second)
    {
        _first = first;
        _second = second;
    }

    public override TimeSpan? IdleDuration => null;

    public override RateLimiterStatistics? GetStatistics() => _second.GetStatistics() ?? _first.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var firstLease = _first.AttemptAcquire(permitCount);
        if (!firstLease.IsAcquired)
            return firstLease;

        var secondLease = _second.AttemptAcquire(permitCount);
        if (!secondLease.IsAcquired)
        {
            firstLease.Dispose();
            return secondLease;
        }

        return new CombinedLease(firstLease, secondLease);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var firstLease = await _first.AcquireAsync(permitCount, cancellationToken).ConfigureAwait(false);
        if (!firstLease.IsAcquired)
            return firstLease;

        RateLimitLease secondLease;
        try
        {
            secondLease = await _second.AcquireAsync(permitCount, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            firstLease.Dispose();
            throw;
        }

        if (!secondLease.IsAcquired)
        {
            firstLease.Dispose();
            return secondLease;
        }

        return new CombinedLease(firstLease, secondLease);
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (disposing)
        {
            _first.Dispose();
            _second.Dispose();
        }
    }

    protected override ValueTask DisposeAsyncCore()
    {
        Dispose(true);
        return default;
    }

    private sealed class CombinedLease : RateLimitLease
    {
        private readonly RateLimitLease _first;
        private readonly RateLimitLease _second;
        private int _disposed;

        public CombinedLease(RateLimitLease first, RateLimitLease second)
        {
            _first = first;
            _second = second;
        }

        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames =>
            _first.MetadataNames.Concat(_second.MetadataNames).Distinct();

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (_second.TryGetMetadata(metadataName, out metadata))
                return true;
            return _first.TryGetMetadata(metadataName, out metadata);
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (disposing)
            {
                _second.Dispose();
                _first.Dispose();
            }
        }
    }
}
