using Application.Exceptions;
using Application.Interfaces.Services;
using Microsoft.AspNetCore.Http;

namespace Application.Common.Security;

public static class CsrfMutationGuard
{
    public const string HeaderName = "X-CSRF-TOKEN";

    public static bool RequiresToken(string? method, string? path)
    {
        var httpMethod = method ?? string.Empty;
        if (HttpMethods.IsGet(httpMethod) ||
            HttpMethods.IsHead(httpMethod) ||
            HttpMethods.IsOptions(httpMethod) ||
            HttpMethods.IsTrace(httpMethod))
            return false;

        var value = path ?? string.Empty;
        if (value.StartsWith("/api/Auth/login", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("/api/Auth/refresh", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    public static void Enforce(
        string? method,
        string? path,
        string? userId,
        bool headerPresent,
        string? headerToken,
        string? signature,
        ICsrfService csrf)
    {
        if (!RequiresToken(method, path))
            return;

        if (string.IsNullOrWhiteSpace(userId))
            throw new UnauthenticatedUserException();

        if (!headerPresent)
            throw new CsrfTokenMissingException();

        if (!csrf.Validate(userId, headerToken ?? string.Empty, signature))
            throw new CsrfTokenInvalidException();
    }
}
