using System.Net;
using API.IntegrationTests.Infrastructure;
using Application.Services.AuthServices;
using Microsoft.EntityFrameworkCore;

namespace API.IntegrationTests.Auth;

/// <summary>
/// MH-SEC-051 baseline HTTP proofs for current secure session behavior (expected green).
/// </summary>
[Collection(AuthHttpCollection.Name)]
public sealed class AuthSessionBaselineTests
{
    private readonly AuthWebApplicationFactory _factory;

    public AuthSessionBaselineTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_success_sets_httponly_cookies_without_tokens_in_body()
    {
        var session = _factory.CreateSession();
        var response = await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CookieHeaderAssertions.AssertAuthCookiesIssued(response);
        CookieHeaderAssertions.AssertRefreshPathIsRoot(response);

        using var doc = await session.ReadJsonAsync(response);
        var json = doc.RootElement.GetRawText();
        Assert.DoesNotContain("eyJ", json, StringComparison.Ordinal); // JWT payload fragment
        Assert.DoesNotContain(AuthTestSeed.ActivePassword, json, StringComparison.Ordinal);
        Assert.True(doc.RootElement.GetProperty("data").TryGetProperty("csrfToken", out _));
    }

    [Fact]
    public async Task Login_failure_does_not_issue_auth_cookies()
    {
        var session = _factory.CreateSession();
        var before = await CountRefreshTokensAsync();
        var response = await session.LoginAsync(AuthTestSeed.ActiveUserName, "Wrong!Pass9");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(CookieHeaderAssertions.FindSetCookie(response, AuthCookieNames.AccessToken));
        Assert.Null(CookieHeaderAssertions.FindSetCookie(response, AuthCookieNames.RefreshToken));
        Assert.Equal(before, await CountRefreshTokensAsync());
    }

    [Fact]
    public async Task Login_unknown_user_returns_401_without_session()
    {
        var session = _factory.CreateSession();
        var response = await session.LoginAsync("missing.user.it", "Wrong!Pass9");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(session.HasCookie(AuthCookieNames.AccessToken));
    }

    [Fact]
    public async Task Refresh_rotates_cookies_and_rejects_reuse()
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);

        var firstRefreshCookie = session.Cookies.GetCookies(session.Client.BaseAddress!)
            [AuthCookieNames.RefreshToken]?.Value;
        Assert.False(string.IsNullOrWhiteSpace(firstRefreshCookie));

        var refresh = await session.RefreshAsync();
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        CookieHeaderAssertions.AssertAuthCookiesIssued(refresh);

        // Replay old refresh value by restoring cookie.
        var container = session.Cookies;
        CookieHeaderAssertions.RemoveCookie(container, session.Client.BaseAddress!, AuthCookieNames.RefreshToken);
        container.Add(session.Client.BaseAddress!, new System.Net.Cookie(AuthCookieNames.RefreshToken, firstRefreshCookie!)
        {
            HttpOnly = true,
            Path = "/"
        });

        var reuse = await session.RefreshAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }

    [Fact]
    public async Task Logout_with_access_and_refresh_ends_current_family_access()
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);

        var logout = await session.LogoutAsync();
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var me = await session.MeAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var refresh = await session.RefreshAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_repeated_does_not_return_500()
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.LogoutAsync()).StatusCode);

        // Second logout may be 401 (no access) — must not be 500.
        var second = await session.LogoutAsync();
        Assert.NotEqual(HttpStatusCode.InternalServerError, second.StatusCode);
    }

    [Fact]
    public async Task Two_sessions_for_same_user_are_isolated_on_full_cookie_logout()
    {
        var first = _factory.CreateSession();
        var second = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await first.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await first.LogoutAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.MeAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.MeAsync()).StatusCode);
    }

    [Fact]
    public async Task Disabled_user_cannot_login_and_reactivation_restores_login()
    {
        await SetUserActiveAsync(AuthTestSeed.InactiveUserName, false);
        var session = _factory.CreateSession();
        var disabledLogin = await session.LoginAsync(AuthTestSeed.InactiveUserName, AuthTestSeed.InactivePassword);
        Assert.Equal(HttpStatusCode.Unauthorized, disabledLogin.StatusCode);

        await SetUserActiveAsync(AuthTestSeed.InactiveUserName, true);
        var enabledLogin = await session.LoginAsync(AuthTestSeed.InactiveUserName, AuthTestSeed.InactivePassword);
        Assert.Equal(HttpStatusCode.OK, enabledLogin.StatusCode);

        // Restore fixture state for other tests.
        await SetUserActiveAsync(AuthTestSeed.InactiveUserName, false);
    }

    [Fact]
    public async Task Change_password_success_invalidates_prior_session()
    {
        var session = _factory.CreateSession();
        var other = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.SecondUserName, AuthTestSeed.SecondPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(AuthTestSeed.SecondUserName, AuthTestSeed.SecondPassword)).StatusCode);

        const string nextPassword = "AuthT3st!Pass9";
        var change = await session.ChangeMyPasswordAsync(AuthTestSeed.SecondPassword, nextPassword, nextPassword);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await other.MeAsync()).StatusCode);

        // Restore password for suite isolation.
        var restore = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await restore.LoginAsync(AuthTestSeed.SecondUserName, nextPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await restore.ChangeMyPasswordAsync(nextPassword, AuthTestSeed.SecondPassword, AuthTestSeed.SecondPassword)).StatusCode);
    }

    [Fact]
    public async Task Admin_reset_password_requires_permission_and_succeeds_for_admin()
    {
        var targetId = await _factory.WithDbAsync(async db =>
            await db.Users.AsNoTracking()
                .Where(u => u.UserName == AuthTestSeed.ActiveUserName)
                .Select(u => u.Id)
                .SingleAsync());

        var denied = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await denied.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);
        var forbidden = await denied.ForbiddenAdminResetProbeAsync(targetId);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var admin = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);
        const string temporary = "Tmp!Reset9Aa";
        var reset = await admin.AdminResetPasswordAsync(targetId, temporary);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var loginTemp = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await loginTemp.LoginAsync(AuthTestSeed.ActiveUserName, temporary)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await loginTemp.ChangeMyPasswordAsync(temporary, AuthTestSeed.ActivePassword, AuthTestSeed.ActivePassword)).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_me_is_401_not_403()
    {
        var session = _factory.CreateSession();
        var me = await session.MeAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var doc = await session.ReadJsonAsync(me);
        Assert.Equal("UNAUTHORIZED_ACCESS", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Audit_login_and_logout_do_not_store_password_or_raw_tokens()
    {
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);
        var refreshValue = session.Cookies.GetCookies(session.Client.BaseAddress!)[AuthCookieNames.RefreshToken]?.Value;
        Assert.Equal(HttpStatusCode.OK, (await session.LogoutAsync()).StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            var events = await db.Set<Domain.Entities.Audit.AuditEvent>()
                .AsNoTracking()
                .OrderByDescending(e => e.EventId)
                .Take(20)
                .ToListAsync();

            Assert.Contains(events, e => e.EventType == "Login" && e.Result == "SUCCESS");
            Assert.Contains(events, e => e.EventType == "Logout");
            foreach (var evt in events)
            {
                Assert.DoesNotContain(AuthTestSeed.ActivePassword, evt.ReasonText ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(AuthTestSeed.ActivePassword, evt.OldValues ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain(AuthTestSeed.ActivePassword, evt.NewValues ?? string.Empty, StringComparison.Ordinal);
                if (!string.IsNullOrEmpty(refreshValue))
                {
                    Assert.DoesNotContain(refreshValue, evt.ReasonText ?? string.Empty, StringComparison.Ordinal);
                    Assert.DoesNotContain(refreshValue, evt.OldValues ?? string.Empty, StringComparison.Ordinal);
                    Assert.DoesNotContain(refreshValue, evt.NewValues ?? string.Empty, StringComparison.Ordinal);
                }
            }
        });
    }

    [Fact]
    public void Factory_database_path_is_temp_isolated_sqlite_file()
    {
        Assert.StartsWith(Path.GetTempPath(), _factory.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mh-sec-051-", _factory.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DefaultConnection", _factory.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> CountRefreshTokensAsync()
        => await _factory.WithDbAsync(db => db.UserRefreshTokens.CountAsync());

    private async Task SetUserActiveAsync(string userName, bool isActive)
    {
        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.UserName == userName);
            user.IsActive = isActive;
            await db.SaveChangesAsync();
        });
    }
}
