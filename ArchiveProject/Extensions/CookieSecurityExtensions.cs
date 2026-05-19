using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Hosting;

namespace ArchiveProject.API.Extensions
{
    public static class CookieSecurityExtensions
    {
        public static IServiceCollection AddCookieSecurity(
            this IServiceCollection services,
            IWebHostEnvironment environment)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                options.Secure = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;

                // ✅ حماية من CSRF بشكل افتراضي (غير مدمّر)
                options.MinimumSameSitePolicy = SameSiteMode.Lax;

                options.HttpOnly = HttpOnlyPolicy.Always;
            });

            return services;
        }

        public static IApplicationBuilder UseCookieSecurity(this IApplicationBuilder app)
        {
            // ✅ مهم جداً إذا السيرفر وراء Proxy (Nginx/Apache/LoadBalancer)
            // حتى Request.IsHttps يصير true ويتعامل صح مع Secure cookies
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            // ✅ تطبيق CookiePolicy (لازم قبل أي شي بيكتب كوكيز)
            app.UseCookiePolicy();

            return app;
        }
    }
}
