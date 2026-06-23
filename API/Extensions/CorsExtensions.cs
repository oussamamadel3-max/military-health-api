namespace API.Extensions
{
    public static class CorsExtensions
    {
        public const string AllowFrontend = "AllowFrontend";

        public static IServiceCollection AddFrontendCors(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var allowedOrigins = configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();

            services.AddCors(options =>
            {
                options.AddPolicy(AllowFrontend, policy =>
                {
                    policy
                        .WithOrigins(allowedOrigins)   // ✅ بدون *
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()             // ✅ لأنك تستخدم Token + Session
                        .WithExposedHeaders(
                            "Content-Disposition",
                            "X-RateLimit-Error-Message",
                            "Retry-After");
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
