using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.HttpOverrides;

namespace ArchiveProject.API.Extensions
{
    public static class CookieSecurityExtensions
    {
        public static IServiceCollection AddCookieSecurity(this IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // ✅ اجبار إرسال الكوكي فقط عبر HTTPS
                options.Secure = CookieSecurePolicy.Always;

                // ✅ حماية من CSRF بشكل افتراضي (غير مدمّر)
                options.MinimumSameSitePolicy = SameSiteMode.Lax;

                // اختياري: سياسة HttpOnly افتراضية (لا تغيّر كوكيز تحتاجها JS)
                options.HttpOnly = HttpOnlyPolicy.None;
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
