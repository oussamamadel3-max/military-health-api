using Domain.Entities.Auth;

namespace Application.Services.AuthServices;

/// <summary>منطق حسابي خالص لـ progressive lockout (قابل للاختبار دون Identity).</summary>
public static class LoginLockoutCalculator
{
    public static int GetRetryAfterSeconds(DateTimeOffset? lockoutEnd, DateTimeOffset utcNow)
    {
        if (lockoutEnd is null)
            return 1;

        var seconds = (lockoutEnd.Value - utcNow).TotalSeconds;
        if (seconds <= 0)
            return 1;

        return Math.Max(1, (int)Math.Ceiling(seconds));
    }

    public static int GetRetryAfterSeconds(User user, DateTimeOffset? utcNow = null)
        => GetRetryAfterSeconds(user.LockoutEnd, utcNow ?? DateTimeOffset.UtcNow);

    public static bool ShouldDecayTier(
        int currentTier,
        DateTimeOffset? lockoutEnd,
        bool isCurrentlyLocked,
        int tierResetAfterSeconds,
        DateTimeOffset utcNow)
    {
        if (currentTier <= 0 || isCurrentlyLocked || lockoutEnd is null)
            return false;

        if (lockoutEnd.Value >= utcNow)
            return false;

        var quietFor = utcNow - lockoutEnd.Value;
        return quietFor.TotalSeconds >= tierResetAfterSeconds;
    }

    public static int ClampTier(int tier, int durationsLength)
    {
        if (durationsLength <= 0)
            return 0;
        return Math.Clamp(tier, 0, durationsLength - 1);
    }

    public static int NextTier(int currentTier, int durationsLength)
    {
        if (durationsLength <= 0)
            return 0;
        var last = durationsLength - 1;
        return Math.Min(ClampTier(currentTier, durationsLength) + 1, last);
    }

    public static int DurationSecondsForTier(int tier, IReadOnlyList<int> durations)
    {
        if (durations.Count == 0)
            return 30;
        return durations[ClampTier(tier, durations.Count)];
    }
}
