using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace API.Extensions
{
    public static class CookieSecurityExtensions
    {
        public static IServiceCollection AddCookieSecurity(
            this IServiceCollection services,
            IWebHostEnvironment environment)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                // Testing uses HTTP WebApplicationFactory hosts (MH-SEC-051); keep Secure=Always for real non-dev hosts.
                options.Secure = environment.IsDevelopment() || environment.IsEnvironment("Testing")
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;

                options.MinimumSameSitePolicy = SameSiteMode.Lax;
                options.HttpOnly = HttpOnlyPolicy.Always;
            });

            return services;
        }

        public static IApplicationBuilder UseCookieSecurity(this IApplicationBuilder app)
        {
            // ForwardedHeaders is configured once in Program via UseAppForwardedHeaders.
            app.UseCookiePolicy();
            return app;
        }
    }
}
