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
        var secure = !_environment.IsDevelopment();
        var accessExpires = DateTimeOffset.UtcNow.AddMinutes(_jwtSettings.ExpirationInMinutes);
        var refreshExpires = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationInDays);

        response.Cookies.Append(AuthCookieNames.AccessToken, accessToken, BuildCookieOptions(accessExpires, secure, path: "/"));
        response.Cookies.Append(AuthCookieNames.RefreshToken, refreshToken, BuildCookieOptions(refreshExpires, secure, path: "/"));
    }

    public void SetCsrfSignatureCookie(HttpResponse response, string signature)
    {
        var secure = !_environment.IsDevelopment();
        var expires = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationInDays);
        response.Cookies.Append(AuthCookieNames.CsrfSignature, signature, BuildCookieOptions(expires, secure, path: "/"));
    }

    public void ClearAuthCookies(HttpResponse response)
    {
        var secure = !_environment.IsDevelopment();
        response.Cookies.Delete(AuthCookieNames.AccessToken, BuildClearOptions(secure));
        response.Cookies.Delete(AuthCookieNames.RefreshToken, BuildClearOptions(secure));
        response.Cookies.Delete(AuthCookieNames.CsrfSignature, BuildClearOptions(secure));
    }

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

    private static CookieOptions BuildClearOptions(bool secure) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
    };
}
