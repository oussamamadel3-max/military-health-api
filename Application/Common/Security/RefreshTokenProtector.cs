using System.Security.Cryptography;
using System.Text;

namespace Application.Common.Security;

public static class RefreshTokenProtector
{
    public static string GenerateRawToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string HashToken(string rawToken)
    {
        if (string.IsNullOrEmpty(rawToken))
            throw new ArgumentException("Refresh token is required.", nameof(rawToken));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }
}
