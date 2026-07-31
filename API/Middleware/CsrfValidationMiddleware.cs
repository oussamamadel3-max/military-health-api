using API.Infrastructure;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using System.Security.Claims;

namespace API.Middleware
{
    public class CsrfValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private const string CsrfHeaderName = "X-CSRF-TOKEN";

        public CsrfValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ICsrfService csrfService,
            AuthCookieService cookieService)
        {
            var method = context.Request.Method;

            // 1) افحص الشروط الاستثنائية التي لا تتطلب CSRF
            if (HttpMethods.IsGet(method) ||
                HttpMethods.IsHead(method) ||
                HttpMethods.IsOptions(method) ||
                HttpMethods.IsTrace(method))
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;

            // مسارات Auth مستثناة من CSRF (Login, Refresh, Logout)
            if (path.StartsWith("/api/Auth/login", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/Auth/refresh", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/Auth/logout", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(userId))
                throw new UnauthenticatedUserException();

            if (!context.Request.Headers.TryGetValue(CsrfHeaderName, out var headerValues))
                throw new CsrfTokenMissingException();

            var csrfToken = headerValues.FirstOrDefault();
            var signature = cookieService.TryGetCsrfSignature(context.Request);

            if (!csrfService.Validate(userId, csrfToken ?? string.Empty, signature))
                throw new CsrfTokenInvalidException();

            await _next(context);
        }
    }
}
