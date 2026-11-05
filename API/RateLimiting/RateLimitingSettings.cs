namespace API.RateLimiting;

public sealed class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    public GlobalRateLimitSettings Global { get; set; } = new();
    public LoginRateLimitSettings Login { get; set; } = new();

    /// <summary>MH-SEC-055 / D3: same shape as Login (sliding window); IP-only partition at policy layer.</summary>
    public LoginRateLimitSettings Refresh { get; set; } = new();

    public ReportsRateLimitSettings Reports { get; set; } = new();
    public PublicRateLimitSettings Public { get; set; } = new();
}

public sealed class GlobalRateLimitSettings
{
    public TokenBucketSettings Authenticated { get; set; } = new();
    public TokenBucketSettings Anonymous { get; set; } = new();
}

public sealed class TokenBucketSettings
{
    public int TokenLimit { get; set; }
    public int TokensPerPeriod { get; set; }
    public int ReplenishmentPeriodSeconds { get; set; }
}

public sealed class LoginRateLimitSettings
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public int SegmentsPerWindow { get; set; }
}

public sealed class ReportsRateLimitSettings
{
    public int ConcurrencyLimit { get; set; }
    public int TokenLimit { get; set; }
    public int TokensPerPeriod { get; set; }
    public int ReplenishmentPeriodSeconds { get; set; }
}

public sealed class PublicRateLimitSettings
{
    public int TokenLimit { get; set; }
    public int TokensPerPeriod { get; set; }
    public int ReplenishmentPeriodSeconds { get; set; }
}
