using Microsoft.Extensions.Options;

namespace API.RateLimiting;

public sealed class RateLimitingSettingsValidator : IValidateOptions<RateLimitingSettings>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingSettings options)
    {
        var errors = new List<string>();

        ValidateTokenBucket("RateLimiting:Global:Authenticated", options.Global.Authenticated, errors);
        ValidateTokenBucket("RateLimiting:Global:Anonymous", options.Global.Anonymous, errors);
        ValidateTokenBucket("RateLimiting:Safety:Authenticated", options.Safety.Authenticated, errors);
        ValidateTokenBucket("RateLimiting:Safety:Anonymous", options.Safety.Anonymous, errors);
        ValidateTokenBucket("RateLimiting:Administrative", options.Administrative, errors);
        ValidateTokenBucket("RateLimiting:ReferenceData", options.ReferenceData, errors);
        ValidateTokenBucket("RateLimiting:Public", options.Public, errors);

        ValidateSlidingWindow("RateLimiting:Login", options.Login, errors);
        ValidateSlidingWindow("RateLimiting:Refresh", options.Refresh, errors);
        ValidateConcurrencyBucket("RateLimiting:Reports", options.Reports, errors);
        ValidateConcurrencyBucket("RateLimiting:HeavyRead", options.HeavyRead, errors);

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateSlidingWindow(string prefix, LoginRateLimitSettings settings, List<string> errors)
    {
        if (settings.PermitLimit <= 0)
            errors.Add($"{prefix}:PermitLimit must be > 0.");
        if (settings.WindowSeconds <= 0)
            errors.Add($"{prefix}:WindowSeconds must be > 0.");
        if (settings.SegmentsPerWindow <= 0)
            errors.Add($"{prefix}:SegmentsPerWindow must be > 0.");
    }

    private static void ValidateTokenBucket(string prefix, TokenBucketSettings settings, List<string> errors)
    {
        if (settings.TokenLimit <= 0)
            errors.Add($"{prefix}:TokenLimit must be > 0.");
        if (settings.TokensPerPeriod <= 0)
            errors.Add($"{prefix}:TokensPerPeriod must be > 0.");
        if (settings.ReplenishmentPeriodSeconds <= 0)
            errors.Add($"{prefix}:ReplenishmentPeriodSeconds must be > 0.");
    }

    private static void ValidateConcurrencyBucket(string prefix, ReportsRateLimitSettings settings, List<string> errors)
    {
        if (settings.ConcurrencyLimit <= 0)
            errors.Add($"{prefix}:ConcurrencyLimit must be > 0.");
        if (settings.TokenLimit <= 0)
            errors.Add($"{prefix}:TokenLimit must be > 0.");
        if (settings.TokensPerPeriod <= 0)
            errors.Add($"{prefix}:TokensPerPeriod must be > 0.");
        if (settings.ReplenishmentPeriodSeconds <= 0)
            errors.Add($"{prefix}:ReplenishmentPeriodSeconds must be > 0.");
    }

    private static void ValidateTokenBucket(string prefix, PublicRateLimitSettings settings, List<string> errors)
    {
        if (settings.TokenLimit <= 0)
            errors.Add($"{prefix}:TokenLimit must be > 0.");
        if (settings.TokensPerPeriod <= 0)
            errors.Add($"{prefix}:TokensPerPeriod must be > 0.");
        if (settings.ReplenishmentPeriodSeconds <= 0)
            errors.Add($"{prefix}:ReplenishmentPeriodSeconds must be > 0.");
    }
}
