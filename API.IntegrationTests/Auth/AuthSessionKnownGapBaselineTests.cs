using System.Net;
using API.IntegrationTests.Infrastructure;
using Application.Services.AuthServices;

namespace API.IntegrationTests.Auth;

/// <summary>
/// MH-SEC-051 documented baseline gaps: assertions encode the *desired* secure outcome.
/// These are expected RED until the owning fix card lands. Do not weaken expectations to force green.
/// </summary>
[Collection(AuthHttpCollection.Name)]
public sealed class AuthSessionKnownGapBaselineTests
{
    private readonly AuthWebApplicationFactory _factory;

    public AuthSessionKnownGapBaselineTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    [Trait("Baseline", "KnownGap:MH-SEC-052")]
    public async Task Logout_without_refresh_cookie_should_reject_subsequent_access()
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);

        var accessValue = session.Cookies.GetCookies(session.Client.BaseAddress!)[AuthCookieNames.AccessToken]?.Value;
        Assert.False(string.IsNullOrWhiteSpace(accessValue));

        session.DropRefreshCookie();
        var logout = await session.LogoutAsync();
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        // Re-attach pre-logout access cookie — proves server-side family invalidation, not just Set-Cookie deletes.
        session.Cookies.Add(session.Client.BaseAddress!, new System.Net.Cookie(AuthCookieNames.AccessToken, accessValue!)
        {
            HttpOnly = true,
            Path = "/"
        });

        // Desired after MH-SEC-052: access family ended even without refresh cookie.
        var me = await session.MeAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    [Trait("Baseline", "KnownGap:MH-SEC-052")]
    public async Task Logout_without_refresh_cookie_should_reject_subsequent_refresh_of_dropped_family()
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);

        // Capture refresh, drop from client, logout via access only, then replay refresh.
        var refreshValue = session.Cookies.GetCookies(session.Client.BaseAddress!)[AuthCookieNames.RefreshToken]?.Value;
        Assert.False(string.IsNullOrWhiteSpace(refreshValue));
        session.DropRefreshCookie();
        Assert.Equal(HttpStatusCode.OK, (await session.LogoutAsync()).StatusCode);

        session.Cookies.Add(session.Client.BaseAddress!, new System.Net.Cookie(AuthCookieNames.RefreshToken, refreshValue!)
        {
            HttpOnly = true,
            Path = "/"
        });
        var refresh = await session.RefreshAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    [Trait("Baseline", "KnownGap:MH-SEC-053")]
    public async Task Change_password_should_reject_reusing_current_password()
    {
        var session = _factory.CreateSession();
        var login = await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var response = await session.ChangeMyPasswordAsync(
            AuthTestSeed.ActivePassword,
            AuthTestSeed.ActivePassword,
            AuthTestSeed.ActivePassword);

        // Desired after MH-SEC-053: reject reuse with a validation/business error (not auth failure).
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(
            (int)response.StatusCode is >= 400 and < 500,
            $"Expected client error rejecting password reuse, got {(int)response.StatusCode}");
    }

    [Fact]
    [Trait("Baseline", "KnownGap:MH-SEC-054")]
    public async Task Refresh_cookie_path_should_be_refresh_endpoint_not_root()
    {
        var session = _factory.CreateSession();
        var login = await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var refreshCookie = CookieHeaderAssertions.FindSetCookie(login, AuthCookieNames.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refreshCookie));

        // Desired after MH-SEC-054 (+ F-12): Path matches Refresh route, not '/'.
        Assert.Contains("path=/api/Auth/Refresh", refreshCookie!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("; path=/;", refreshCookie!, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            refreshCookie!.EndsWith("path=/", StringComparison.OrdinalIgnoreCase)
            || refreshCookie.Contains("; path=/;", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(refreshCookie, @";\s*path=/(;|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            "Refresh cookie must not use Path=/ after MH-SEC-054");
    }

    [Fact]
    [Trait("Baseline", "KnownGap:MH-SEC-055")]
    public async Task Refresh_endpoint_should_return_429_when_rate_limit_exceeded()
    {
        // Desired after MH-SEC-055: dedicated Refresh rate limit. Currently no Refresh policy exists.
        // This probe floods Refresh; without a policy it should NOT stay 200 forever under a limit —
        // assert that at least one response becomes 429 within the burst (expected fail today).
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);

        HttpStatusCode? saw429 = null;
        for (var i = 0; i < 40; i++)
        {
            var response = await session.RefreshAsync();
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                saw429 = response.StatusCode;
                break;
            }

            if (response.StatusCode != HttpStatusCode.OK)
                break;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, saw429);
    }

    [Fact]
    [Trait("Baseline", "KnownGap:MH-SEC-061")]
    public async Task Locked_account_login_should_look_like_unknown_user_401()
    {
        // Desired after MH-SEC-061: locked == unknown at client Status/Code (no 429 + Retry-After).
        await _factory.WithDbAsync(async db =>
        {
            var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .SingleAsync(db.Users, u => u.UserName == AuthTestSeed.ActiveUserName);
            user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
            user.AccessFailedCount = 0;
            await db.SaveChangesAsync();
        });

        try
        {
            var session = _factory.CreateSession();
            var locked = await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword);
            var unknown = await session.LoginAsync("missing.user.lockcheck", "Wrong!Pass9");

            Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
            Assert.Equal(unknown.StatusCode, locked.StatusCode);
            Assert.False(locked.Headers.Contains("Retry-After"));
        }
        finally
        {
            await _factory.WithDbAsync(async db =>
            {
                var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .SingleAsync(db.Users, u => u.UserName == AuthTestSeed.ActiveUserName);
                user.LockoutEnd = null;
                user.AccessFailedCount = 0;
                user.LoginLockoutTier = 0;
                await db.SaveChangesAsync();
            });
        }
    }
}
