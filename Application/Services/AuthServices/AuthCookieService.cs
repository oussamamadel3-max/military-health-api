using Application.Dtos.AuthDtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Application.Services.AuthServices;

public static class AuthCookieNames
{
    public const string AccessToken = "access_token";
    public const string RefreshToken = "refresh_token";
    public const string CsrfSignature = "csrf_sig";

    /// <summary>Root path for Access / CSRF cookies.</summary>
    public const string RootPath = "/";

    /// <summary>
    /// MH-SEC-054 / F-12: Refresh cookie Path matches the Refresh endpoint route
    /// (<c>api/[controller]=Auth</c> + <c>[action]=Refresh</c>).
    /// </summary>
    public const string RefreshTokenPath = "/api/Auth/Refresh";
}

public class AuthCookieService
{
    private readonly JwtSettings _jwtSettings;
    private readonly IWebHostEnvironment _environment;

    public AuthCookieService(IOptions<JwtSettings> jwtOptions, IWebHostEnvironment environment)
    {
        _jwtSettings = jwtOptions.Value;
        _environment = environment;
    }

    public void SetAuthCookies(HttpResponse response, string accessToken, string refreshToken)
    {
        var secure = RequiresSecureCookies();
        var accessExpires = DateTimeOffset.UtcNow.AddMinutes(_jwtSettings.ExpirationInMinutes);
        var refreshExpires = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationInDays);

        response.Cookies.Append(
            AuthCookieNames.AccessToken,
            accessToken,
            BuildCookieOptions(accessExpires, secure, AuthCookieNames.RootPath));

        // Drop legacy Path=/ refresh copies so browsers never keep two same-name cookies after upgrade.
        response.Cookies.Delete(AuthCookieNames.RefreshToken, BuildClearOptions(secure, AuthCookieNames.RootPath));
        response.Cookies.Append(
            AuthCookieNames.RefreshToken,
            refreshToken,
            BuildCookieOptions(refreshExpires, secure, AuthCookieNames.RefreshTokenPath));
    }

    public void SetCsrfSignatureCookie(HttpResponse response, string signature)
    {
        var secure = RequiresSecureCookies();
        var expires = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationInDays);
        response.Cookies.Append(
            AuthCookieNames.CsrfSignature,
            signature,
            BuildCookieOptions(expires, secure, AuthCookieNames.RootPath));
    }

    /// <summary>
    /// Clears Access/CSRF on <c>/</c> and Refresh on both legacy <c>/</c> and <see cref="AuthCookieNames.RefreshTokenPath"/>
    /// (MH-SEC-054 dual-delete upgrade contract).
    /// </summary>
    public void ClearAuthCookies(HttpResponse response)
    {
        var secure = RequiresSecureCookies();
        response.Cookies.Delete(AuthCookieNames.AccessToken, BuildClearOptions(secure, AuthCookieNames.RootPath));
        response.Cookies.Delete(AuthCookieNames.RefreshToken, BuildClearOptions(secure, AuthCookieNames.RootPath));
        response.Cookies.Delete(AuthCookieNames.RefreshToken, BuildClearOptions(secure, AuthCookieNames.RefreshTokenPath));
        response.Cookies.Delete(AuthCookieNames.CsrfSignature, BuildClearOptions(secure, AuthCookieNames.RootPath));
    }

    /// <summary>
    /// Production/Staging keep Secure cookies. Development and Testing allow HTTP test hosts (MH-SEC-051).
    /// </summary>
    private bool RequiresSecureCookies()
        => !_environment.IsDevelopment()
           && !_environment.IsEnvironment("Testing");

    public string? TryGetRefreshToken(HttpRequest request)
        => request.Cookies.TryGetValue(AuthCookieNames.RefreshToken, out var token) ? token : null;

    public string? TryGetAccessToken(HttpRequest request)
        => request.Cookies.TryGetValue(AuthCookieNames.AccessToken, out var token) ? token : null;

    public string? TryGetCsrfSignature(HttpRequest request)
        => request.Cookies.TryGetValue(AuthCookieNames.CsrfSignature, out var sig) ? sig : null;

    private static CookieOptions BuildCookieOptions(DateTimeOffset expires, bool secure, string path) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = path,
        Expires = expires,
        IsEssential = true,
    };

    private static CookieOptions BuildClearOptions(bool secure, string path) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = path,
        IsEssential = true,
    };
}
