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

    public static string? FindSetCookie(HttpResponseMessage response, string cookieName)
        => GetSetCookieHeaders(response)
            .FirstOrDefault(v => v.StartsWith(cookieName + "=", StringComparison.OrdinalIgnoreCase));

    public static void AssertAuthCookiesIssued(HttpResponseMessage response)
    {
        var access = FindSetCookie(response, AuthCookieNames.AccessToken);
        var refresh = FindSetCookie(response, AuthCookieNames.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(access), "Missing access_token Set-Cookie");
        Assert.False(string.IsNullOrWhiteSpace(refresh), "Missing refresh_token Set-Cookie");
        Assert.Contains("httponly", access!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", refresh!, StringComparison.OrdinalIgnoreCase);
    }

    public static void AssertRefreshPathIsRoot(HttpResponseMessage response)
    {
        var refresh = FindSetCookie(response, AuthCookieNames.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refresh));
        Assert.True(
            Regex.IsMatch(refresh!, @";\s*path=/($|;|\s)", RegexOptions.IgnoreCase)
            || refresh!.Contains("; path=/", StringComparison.OrdinalIgnoreCase)
            || refresh.Contains(";path=/", StringComparison.OrdinalIgnoreCase),
            $"Expected Path=/ on refresh cookie (current contract), got: {refresh}");
    }

    public static void RemoveCookie(CookieContainer container, Uri baseAddress, string name)
    {
        foreach (var cookie in container.GetCookies(baseAddress).Cast<Cookie>().Where(c => c.Name == name).ToList())
            cookie.Expired = true;
    }
}
