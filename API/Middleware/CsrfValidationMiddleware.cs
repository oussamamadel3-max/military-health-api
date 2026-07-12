using API.Extensions;
using Application.Common.Results;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using System.Security.Claims;

namespace API.Middleware
{
    public class CsrfValidationMiddleware
    {
        public const string CsrfHeaderName = "X-CSRF-TOKEN";

        private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            HttpMethods.Get,
            HttpMethods.Head,
            HttpMethods.Options,
            HttpMethods.Trace,
        };

        private static readonly string[] ExemptPathPrefixes =
        {
            "/api/auth/login",
            "/api/auth/refresh",
        };

        private readonly RequestDelegate _next;

        public CsrfValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ICsrfService csrfService,
            AuthCookieService cookieService)
        {
            if (ShouldSkip(context))
            {
                await _next(context);
                return;
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(userId))
            {
                await context.Response.WriteResultAsync(
                    StatusCodes.Status401Unauthorized,
                    Result.Failure(new[] { "غير مصرح." }));
                return;
            }

            if (!context.Request.Headers.TryGetValue(CsrfHeaderName, out var headerValues))
            {
                await context.Response.WriteResultAsync(
                    StatusCodes.Status403Forbidden,
                    Result.Failure(new[] { "رمز CSRF مفقود." }));
                return;
            }

            var csrfToken = headerValues.FirstOrDefault();
            var signature = cookieService.TryGetCsrfSignature(context.Request);

            if (!csrfService.Validate(userId, csrfToken ?? string.Empty, signature))
            {
                await context.Response.WriteResultAsync(
                    StatusCodes.Status403Forbidden,
                    Result.Failure(new[] { "رمز CSRF غير صالح." }));
                return;
            }

            await _next(context);
        }

        private static bool ShouldSkip(HttpContext context)
        {
            if (SafeMethods.Contains(context.Request.Method))
                return true;

            var path = context.Request.Path.Value ?? string.Empty;
            var normalized = path.TrimEnd('/').ToLowerInvariant();

            foreach (var prefix in ExemptPathPrefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
