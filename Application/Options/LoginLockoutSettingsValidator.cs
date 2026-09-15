using Microsoft.Extensions.Options;

namespace Application.Options;

public sealed class LoginLockoutSettingsValidator : IValidateOptions<LoginLockoutSettings>
{
    public ValidateOptionsResult Validate(string? name, LoginLockoutSettings options)
    {
        var errors = new List<string>();

        if (options.MaxFailedAccessAttempts <= 0)
            errors.Add("LoginLockout:MaxFailedAccessAttempts must be > 0.");

        if (options.DurationsSeconds is null || options.DurationsSeconds.Length == 0)
            errors.Add("LoginLockout:DurationsSeconds must contain at least one value.");
        else
        {
            for (var i = 0; i < options.DurationsSeconds.Length; i++)
            {
                if (options.DurationsSeconds[i] <= 0)
                    errors.Add($"LoginLockout:DurationsSeconds[{i}] must be > 0.");
            }
        }

        if (options.TierResetAfterSeconds <= 0)
            errors.Add("LoginLockout:TierResetAfterSeconds must be > 0.");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
