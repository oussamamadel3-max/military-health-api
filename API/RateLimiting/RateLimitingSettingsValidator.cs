using Microsoft.Extensions.Options;

namespace API.RateLimiting;

public sealed class RateLimitingSettingsValidator : IValidateOptions<RateLimitingSettings>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingSettings options)
    {
        var errors = new List<string>();

        ValidateTokenBucket("RateLimiting:Global:Authenticated", options.Global.Authenticated, errors);
        ValidateTokenBucket("RateLimiting:Global:Anonymous", options.Global.Anonymous, errors);
        ValidateTokenBucket("RateLimiting:Public", options.Public, errors);

        if (options.Login.PermitLimit <= 0)
            errors.Add("RateLimiting:Login:PermitLimit must be > 0.");
        if (options.Login.WindowSeconds <= 0)
            errors.Add("RateLimiting:Login:WindowSeconds must be > 0.");
        if (options.Login.SegmentsPerWindow <= 0)
            errors.Add("RateLimiting:Login:SegmentsPerWindow must be > 0.");

        if (options.Reports.ConcurrencyLimit <= 0)
            errors.Add("RateLimiting:Reports:ConcurrencyLimit must be > 0.");
        if (options.Reports.TokenLimit <= 0)
            errors.Add("RateLimiting:Reports:TokenLimit must be > 0.");
        if (options.Reports.TokensPerPeriod <= 0)
            errors.Add("RateLimiting:Reports:TokensPerPeriod must be > 0.");
        if (options.Reports.ReplenishmentPeriodSeconds <= 0)
            errors.Add("RateLimiting:Reports:ReplenishmentPeriodSeconds must be > 0.");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
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
