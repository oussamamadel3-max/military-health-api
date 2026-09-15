using Application.Services.AuthServices;
using Xunit;

namespace Application.Tests;

public class LoginLockoutCalculatorTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData(0.1, 1)]
    [InlineData(0.9, 1)]
    [InlineData(1.0, 1)]
    [InlineData(1.1, 2)]
    [InlineData(29.1, 30)]
    [InlineData(30.0, 30)]
    public void GetRetryAfterSeconds_UsesCeil_AndMinimumOne(double? remainingSeconds, int expected)
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        DateTimeOffset? end = remainingSeconds is null
            ? null
            : now.AddSeconds(remainingSeconds.Value);

        var actual = LoginLockoutCalculator.GetRetryAfterSeconds(end, now);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetRetryAfterSeconds_WhenLockoutAlreadyExpired_ReturnsOne()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var end = now.AddSeconds(-5);
        Assert.Equal(1, LoginLockoutCalculator.GetRetryAfterSeconds(end, now));
    }

    [Fact]
    public void ShouldDecayTier_WhenQuietPeriodElapsed_ReturnsTrue()
    {
        var now = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
        var lockoutEnd = now.AddHours(-25);
        Assert.True(LoginLockoutCalculator.ShouldDecayTier(
            currentTier: 4,
            lockoutEnd: lockoutEnd,
            isCurrentlyLocked: false,
            tierResetAfterSeconds: 86400,
            utcNow: now));
    }

    [Fact]
    public void ShouldDecayTier_WhenStillLocked_ReturnsFalse()
    {
        var now = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
        Assert.False(LoginLockoutCalculator.ShouldDecayTier(
            currentTier: 4,
            lockoutEnd: now.AddMinutes(5),
            isCurrentlyLocked: true,
            tierResetAfterSeconds: 86400,
            utcNow: now));
    }

    [Fact]
    public void DurationAndNextTier_FollowLadder_AndCap()
    {
        int[] durations = [30, 60, 120, 300, 900];
        Assert.Equal(30, LoginLockoutCalculator.DurationSecondsForTier(0, durations));
        Assert.Equal(1, LoginLockoutCalculator.NextTier(0, durations.Length));
        Assert.Equal(900, LoginLockoutCalculator.DurationSecondsForTier(4, durations));
        Assert.Equal(4, LoginLockoutCalculator.NextTier(4, durations.Length));
        Assert.Equal(4, LoginLockoutCalculator.NextTier(99, durations.Length));
    }
}
