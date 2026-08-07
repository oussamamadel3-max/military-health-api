using API.Infrastructure;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
            AuthCookieService cookieService,
            IWebHostEnvironment environment,
            IConfiguration configuration)
        {
            // 0) تعطيل فحص الـ CSRF إذا كان مفعل في بيئة التطوير
            var disableCsrf = environment.IsDevelopment()
                && (configuration.GetValue<bool>("Auth:DisableCsrfValidation")
                    || configuration.GetValue<bool>("AuthDevelopmentSettings:DisableCsrfValidation"));

            if (disableCsrf)
            {
                await _next(context);
                return;
            }

            var method = context.Request.Method;

            // 1) افحص الطرق الاستثنائية القابلة للقراءة فقط التي لا تتطلب CSRF
            if (HttpMethods.IsGet(method) ||
                HttpMethods.IsHead(method) ||
                HttpMethods.IsOptions(method) ||
                HttpMethods.IsTrace(method))
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;

            // 2) مسارات Auth المستثناة من CSRF (Login و Refresh فقط، بينما Logout محمي)
            if (path.StartsWith("/api/Auth/login", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/api/Auth/refresh", StringComparison.OrdinalIgnoreCase))
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
