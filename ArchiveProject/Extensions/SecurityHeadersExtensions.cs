namespace ArchiveProject.API.Extensions
{
    public static class SecurityHeadersExtensions
    {
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    // منع Clickjacking
                    if (!context.Response.Headers.ContainsKey("X-Frame-Options"))
                        context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

                    // CSP frame-ancestors
                    if (!context.Response.Headers.ContainsKey("Content-Security-Policy"))
                        context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'self';";

                    // منع MIME sniffing
                    if (!context.Response.Headers.ContainsKey("X-Content-Type-Options"))
                        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

                    return Task.CompletedTask;
                });

                await next();
            });
        }
    }
}
