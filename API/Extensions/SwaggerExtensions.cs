using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.OpenApi.Models;

namespace API.Extensions
{
    public static class SwaggerExtensions
    {
        private const string DarkThemeResourceName = "API.wwwroot.swagger_ui.dark.css";

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
                    Description = "Backend REST API for identity, authentication, and reporting."
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter 'Bearer' [space] and then your valid token"
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
                        new string[] {}
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

            var darkThemeCss = LoadDarkThemeCss(environment);

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v2/swagger.json", "Identity Project API v2");
                c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
                c.DefaultModelsExpandDepth(-1);

                if (!string.IsNullOrWhiteSpace(darkThemeCss))
                    c.HeadContent = $"<style id=\"swagger-dark-theme\">{darkThemeCss}</style>";
            });
            return app;
        }

        private static string LoadDarkThemeCss(IWebHostEnvironment environment)
        {
            var fileCandidates = new[]
            {
                Path.Combine(environment.WebRootPath ?? string.Empty, "swagger-ui", "dark.css"),
                Path.Combine(environment.ContentRootPath, "wwwroot", "swagger-ui", "dark.css"),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", "swagger-ui", "dark.css")
            };

            foreach (var path in fileCandidates)
            {
                if (File.Exists(path))
                    return File.ReadAllText(path);
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DarkThemeResourceName);
            if (stream is null)
                return string.Empty;

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
