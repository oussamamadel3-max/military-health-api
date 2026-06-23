using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Services.Auth;

public class CsrfService : ICsrfService
{
    private readonly byte[] _secretKey;
    private readonly AuthCookieService _cookieService;

    public CsrfService(IOptions<JwtSettings> jwtOptions, AuthCookieService cookieService)
    {
        _secretKey = Encoding.UTF8.GetBytes(jwtOptions.Value.Key);
        _cookieService = cookieService;
    }

    public string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string ComputeSignature(string userId, string csrfToken)
    {
        var payload = $"{userId}|{csrfToken}";
        using var hmac = new HMACSHA256(_secretKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    public bool Validate(string userId, string csrfToken, string? signatureFromCookie)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(csrfToken) || string.IsNullOrWhiteSpace(signatureFromCookie))
            return false;

        var expected = ComputeSignature(userId, csrfToken);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureFromCookie));
    }

    public void IssueCsrf(HttpResponse response, string userId, out string csrfToken)
    {
        csrfToken = GenerateToken();
        var signature = ComputeSignature(userId, csrfToken);
        _cookieService.SetCsrfSignatureCookie(response, signature);
    }
}
