using Application.Services.AuthServices;
using System.Net;
using System.Text.RegularExpressions;

namespace API.IntegrationTests.Infrastructure;

public static class CookieHeaderAssertions
{
    public static IReadOnlyList<string> GetSetCookieHeaders(HttpResponseMessage response)
    {
        var headers = new List<string>();
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            headers.AddRange(values);
        return headers;
    }

    /// <summary>
    /// Finds a Set-Cookie that carries a non-empty value (skips delete markers <c>name=;</c>).
    /// </summary>
    public static string? FindSetCookie(HttpResponseMessage response, string cookieName)
        => GetSetCookieHeaders(response)
            .FirstOrDefault(v =>
                v.StartsWith(cookieName + "=", StringComparison.OrdinalIgnoreCase)
                && !v.StartsWith(cookieName + "=;", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> FindAllSetCookies(HttpResponseMessage response, string cookieName)
        => GetSetCookieHeaders(response)
            .Where(v => v.StartsWith(cookieName + "=", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static void AssertAuthCookiesIssued(HttpResponseMessage response)
    {
        var access = FindSetCookie(response, AuthCookieNames.AccessToken);
        var refresh = FindSetCookie(response, AuthCookieNames.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(access), "Missing access_token Set-Cookie");
        Assert.False(string.IsNullOrWhiteSpace(refresh), "Missing refresh_token Set-Cookie");
        Assert.Contains("httponly", access!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", refresh!, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertRefreshPathIsEndpoint(HttpResponseMessage response)
    {
        var refresh = FindSetCookie(response, AuthCookieNames.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refresh));
        Assert.Contains(
            "path=" + AuthCookieNames.RefreshTokenPath,
            refresh!,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(
            HasExactRootPath(refresh!),
            "Refresh cookie must not use Path=/ after MH-SEC-054");
    }

    public static void AssertRefreshLegacyRootClearedOnIssue(HttpResponseMessage response)
    {
        var refreshHeaders = FindAllSetCookies(response, AuthCookieNames.RefreshToken);
        Assert.Contains(
            refreshHeaders,
            h => h.StartsWith(AuthCookieNames.RefreshToken + "=;", StringComparison.OrdinalIgnoreCase)
                 && HasExactRootPath(h));
    }

    public static void AssertRefreshDualDeleteOnClear(HttpResponseMessage response)
    {
        var refreshHeaders = FindAllSetCookies(response, AuthCookieNames.RefreshToken)
            .Where(h => h.StartsWith(AuthCookieNames.RefreshToken + "=;", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Contains(refreshHeaders, HasExactRootPath);
        Assert.Contains(
            refreshHeaders,
            h => h.Contains("path=" + AuthCookieNames.RefreshTokenPath, StringComparison.OrdinalIgnoreCase));
    }

    public static void RemoveCookie(CookieContainer container, Uri baseAddress, string name)
    {
        foreach (var cookie in EnumerateCookies(container, baseAddress, name).ToList())
            cookie.Expired = true;
    }

    /// <summary>
    /// CookieContainer.GetCookies(root) omits Path-scoped refresh cookies — query both root and Refresh path.
    /// </summary>
    public static IEnumerable<Cookie> EnumerateCookies(CookieContainer container, Uri baseAddress, string? name = null)
    {
        var uris = new[]
        {
            baseAddress,
            new Uri(baseAddress, AuthCookieNames.RefreshTokenPath)
        };

        foreach (var uri in uris.Distinct())
        {
            foreach (var cookie in container.GetCookies(uri).Cast<Cookie>())
            {
                if (name is null || cookie.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    yield return cookie;
            }
        }
    }

    public static string? TryGetCookieValue(CookieContainer container, Uri baseAddress, string name)
        => EnumerateCookies(container, baseAddress, name)
            .FirstOrDefault(c => !c.Expired && !string.IsNullOrEmpty(c.Value))
            ?.Value;

    public static Cookie? TryGetLiveCookie(CookieContainer container, Uri baseAddress, string name)
        => EnumerateCookies(container, baseAddress, name)
            .FirstOrDefault(c => !c.Expired && !string.IsNullOrEmpty(c.Value));

    private static bool HasExactRootPath(string setCookie)
        => Regex.IsMatch(setCookie, @";\s*path=/(;|$)", RegexOptions.IgnoreCase)
           || setCookie.EndsWith("path=/", StringComparison.OrdinalIgnoreCase);
}
