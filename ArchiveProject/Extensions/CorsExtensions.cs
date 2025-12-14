
namespace ArchiveProject.API.Extensions
{
    public static class CorsExtensions
    {
        private const string AllowFrontend = "AllowFrontend";

        public static IServiceCollection AddFrontendCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy(AllowFrontend, policy =>
                {
                    policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .WithExposedHeaders("X-RateLimit-Error-Message", "Retry-After");
                });
            });
            return services;
        }

        public static IApplicationBuilder UseFrontendCors(this IApplicationBuilder app)
        {
            app.UseCors(AllowFrontend);
            return app;
        }
    }
}

