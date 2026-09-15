using Microsoft.AspNetCore.Hosting;
using Microsoft.OpenApi.Models;

namespace API.Extensions
{
    public static class SwaggerExtensions
    {
        public static IServiceCollection AddSwaggerDocumentation(
            this IServiceCollection services,
            IWebHostEnvironment environment)
        {
            if (!environment.IsDevelopment())
                return services;

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v2", new OpenApiInfo
                {
                    Version = "v2",
                    Title = "Identity Project API",
                    Description =
                        "Backend REST API for identity, authentication, and reporting.\n\n" +
                        "**Swagger (Development)**\n" +
                        "1. Call `POST /api/Auth/Login` with username and password.\n" +
                        "2. Open DevTools → Application → Cookies and copy `access_token`.\n" +
                        "3. Click **Authorize**, enter `Bearer <access_token>` (include the word Bearer).\n" +
                        "4. Test any endpoint. CSRF is disabled in Development via appsettings.Development.json."
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Development: paste `Bearer` + space + value of the `access_token` cookie after Login."
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
            });
            return services;
        }

        public static IApplicationBuilder UseSwaggerDocumentation(
            this IApplicationBuilder app,
            IWebHostEnvironment environment)
        {
            if (!environment.IsDevelopment())
                return app;

            // منع كاش المتصفح لملفات Swagger (نسخ قديمة من index.js كانت تسبب صفحة فارغة).
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/swagger"))
                {
                    context.Response.OnStarting(() =>
                    {
                        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                        context.Response.Headers.Pragma = "no-cache";
                        context.Response.Headers.Remove("ETag");
                        context.Response.Headers.Remove("Last-Modified");
                        return Task.CompletedTask;
                    });
                }

                await next();
            });

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("v2/swagger.json", "Identity Project API v2");
                c.RoutePrefix = "swagger";
                c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
                c.DefaultModelsExpandDepth(-1);
                c.EnableDeepLinking();
                c.DisplayRequestDuration();
                c.ConfigObject.TryItOutEnabled = true;

                // صفحة مخصّصة بدون JSON.parse الهش في index.js المولَّد من Swashbuckle
                var indexPath = Path.Combine(
                    environment.ContentRootPath,
                    "wwwroot",
                    "swagger-ui",
                    "index.html");
                c.IndexStream = () => File.OpenRead(indexPath);
            });

            return app;
        }
    }
}
