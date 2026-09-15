using Application.Interfaces.Services;
using Application.Options;
using Domain.Entities.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services.AuthServices;

public sealed class LoginLockoutService : ILoginLockoutService
{
    private const int MaxConcurrencyRetries = 3;

    private readonly UserManager<User> _userManager;
    private readonly IOptions<LoginLockoutSettings> _options;
    private readonly ILogger<LoginLockoutService> _logger;

    public LoginLockoutService(
        UserManager<User> userManager,
        IOptions<LoginLockoutSettings> options,
        ILogger<LoginLockoutService> logger)
    {
        _userManager = userManager;
        _options = options;
        _logger = logger;
    }

    public int GetRetryAfterSeconds(User user)
        => LoginLockoutCalculator.GetRetryAfterSeconds(user);

    public async Task EnsureTierDecayAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var settings = _options.Value;
        var isLocked = await _userManager.IsLockedOutAsync(user);
        if (!LoginLockoutCalculator.ShouldDecayTier(
                user.LoginLockoutTier,
                user.LockoutEnd,
                isLocked,
                settings.TierResetAfterSeconds,
                DateTimeOffset.UtcNow))
        {
            return;
        }

        user.LoginLockoutTier = 0;
        var result = await _userManager.UpdateAsync(user);
        EnsureSucceeded(result, nameof(_userManager.UpdateAsync), user.Id);
    }

    public async Task<int> ApplyProgressiveLockoutAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var user = await _userManager.FindByIdAsync(userId)
                ?? throw new InvalidOperationException($"User '{userId}' was not found during lockout apply.");

            await EnsureTierDecayAsync(user, cancellationToken);
            user = await _userManager.FindByIdAsync(userId)
                ?? throw new InvalidOperationException($"User '{userId}' was not found during lockout apply.");

            // طلب منافس أنهى Apply بالفعل (صفّر AccessFailedCount بعد الحظر التصاعدي).
            if (await _userManager.IsLockedOutAsync(user) && user.AccessFailedCount == 0)
                return GetRetryAfterSeconds(user);

            var settings = _options.Value;
            var durations = settings.DurationsSeconds;
            var tier = LoginLockoutCalculator.ClampTier(user.LoginLockoutTier, durations.Length);
            var durationSeconds = LoginLockoutCalculator.DurationSecondsForTier(tier, durations);
            var lockoutEnd = DateTimeOffset.UtcNow.AddSeconds(durationSeconds);
            user.LoginLockoutTier = LoginLockoutCalculator.NextTier(tier, durations.Length);

            var setResult = await _userManager.SetLockoutEndDateAsync(user, lockoutEnd);
            if (!setResult.Succeeded)
            {
                if (IsConcurrencyFailure(setResult))
                {
                    _logger.LogWarning(
                        "Login lockout concurrency on SetLockoutEndDateAsync for user {UserId}, retry {Attempt}",
                        userId, attempt + 1);
                    continue;
                }

                EnsureSucceeded(setResult, nameof(_userManager.SetLockoutEndDateAsync), userId);
            }

            user = await _userManager.FindByIdAsync(userId)
                ?? throw new InvalidOperationException($"User '{userId}' was not found after lockout set.");

            var resetResult = await _userManager.ResetAccessFailedCountAsync(user);
            if (!resetResult.Succeeded)
            {
                if (IsConcurrencyFailure(resetResult))
                {
                    _logger.LogWarning(
                        "Login lockout concurrency on ResetAccessFailedCountAsync for user {UserId}, retry {Attempt}",
                        userId, attempt + 1);
                    continue;
                }

                EnsureSucceeded(resetResult, nameof(_userManager.ResetAccessFailedCountAsync), userId);
            }

            user = await _userManager.FindByIdAsync(userId)
                ?? throw new InvalidOperationException($"User '{userId}' was not found after lockout reset.");

            return GetRetryAfterSeconds(user);
        }

        var finalUser = await _userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User '{userId}' was not found after lockout retries.");

        if (await _userManager.IsLockedOutAsync(finalUser))
            return GetRetryAfterSeconds(finalUser);

        throw new InvalidOperationException(
            $"Unable to apply progressive login lockout for user '{userId}' after concurrency retries.");
    }

    public async Task ResetAfterSuccessfulLoginAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var resetFails = await _userManager.ResetAccessFailedCountAsync(user);
        EnsureSucceeded(resetFails, nameof(_userManager.ResetAccessFailedCountAsync), user.Id);

        if (user.LoginLockoutTier != 0)
        {
            user.LoginLockoutTier = 0;
            var update = await _userManager.UpdateAsync(user);
            EnsureSucceeded(update, nameof(_userManager.UpdateAsync), user.Id);
        }

        // Clear any residual lockout end after successful auth.
        if (user.LockoutEnd is not null)
        {
            var clear = await _userManager.SetLockoutEndDateAsync(user, null);
            EnsureSucceeded(clear, nameof(_userManager.SetLockoutEndDateAsync), user.Id);
        }
    }

    private static bool IsConcurrencyFailure(IdentityResult result)
        => result.Errors.Any(e =>
            string.Equals(e.Code, "ConcurrencyFailure", StringComparison.OrdinalIgnoreCase) ||
            (e.Description?.Contains("optimistic concurrency", StringComparison.OrdinalIgnoreCase) ?? false));

    private static void EnsureSucceeded(IdentityResult result, string operation, string userId)
    {
        if (result.Succeeded)
            return;

        var details = string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Description}"));
        throw new InvalidOperationException(
            $"Identity {operation} failed for user '{userId}': {details}");
    }
}
