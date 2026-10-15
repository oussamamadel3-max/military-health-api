using Application.Common.Security;
using Application.Exceptions;

namespace API.Middleware;

/// <summary>
/// يمنع المسارات التشغيلية عندما يحمل التوكن مطالبة إلزام تغيير كلمة المرور.
/// </summary>
public sealed class MustChangePasswordMiddleware
{
    private static readonly PathString[] AllowedExact =
    {
        new("/api/Auth/Login"),
        new("/api/Auth/Refresh"),
        new("/api/Auth/Logout"),
        new("/api/Auth/me"),
        new("/api/Auth/csrf"),
        new("/api/User/ChangeMyPassword"),
        new("/health")
    };

    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresPasswordChange(context) && !IsAllowed(context.Request))
            throw new PasswordChangeRequiredException(MustChangePasswordClaims.RequiredMessage);

        await _next(context);
    }

    private static bool RequiresPasswordChange(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return false;

        var value = context.User.FindFirst(MustChangePasswordClaims.ClaimType)?.Value;
        return string.Equals(value, MustChangePasswordClaims.RequiredValue, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowed(HttpRequest request)
    {
        if (HttpMethods.IsOptions(request.Method) || HttpMethods.IsHead(request.Method))
            return true;

        var path = request.Path;
        foreach (var allowed in AllowedExact)
        {
            if (path.Equals(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
