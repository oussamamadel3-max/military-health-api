using API.Infrastructure;
using Application.Common.Security;
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

            var headerPresent = context.Request.Headers.TryGetValue(CsrfMutationGuard.HeaderName, out var headerValues);
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.User.FindFirstValue("sub");

            CsrfMutationGuard.Enforce(
                context.Request.Method,
                context.Request.Path.Value,
                userId,
                headerPresent,
                headerPresent ? headerValues.FirstOrDefault() : null,
                cookieService.TryGetCsrfSignature(context.Request),
                csrfService);

            await _next(context);
        }
    }
}
