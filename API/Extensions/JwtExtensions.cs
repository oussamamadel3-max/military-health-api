using Application.Common.Results;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Repositories;
using Application.Services.AuthServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace API.Extensions
{
    public static class JwtExtensions
    {
        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            var jwtSettingsSection = configuration.GetSection("JwtSettings");
            services.Configure<JwtSettings>(jwtSettingsSection);

            var jwtSettings = jwtSettingsSection.Get<JwtSettings>();
            services.AddSingleton(sp => sp.GetRequiredService<IOptions<JwtSettings>>().Value);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Cookies.TryGetValue(AuthCookieNames.AccessToken, out var token))
                        {
                            context.Token = token;
                            return Task.CompletedTask;
                        }

                        var environment = context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
                        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                        var allowBearer = environment.IsDevelopment()
                            && configuration.GetValue<bool>($"{AuthDevelopmentSettings.SectionName}:{nameof(AuthDevelopmentSettings.AllowBearerTokenInDevelopment)}");

                        if (allowBearer)
                        {
                            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                                context.Token = authHeader["Bearer ".Length..].Trim();
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var userRepository = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                        var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                   ?? context.Principal?.FindFirst("sub")?.Value;

                        if (string.IsNullOrEmpty(userId))
                        {
                            context.Fail("User ID not found in token");
                            return;
                        }

                        var user = await userRepository.FindByIdAsync(userId);
                        if (user == null || !user.IsActive)
                        {
                            context.Fail("User not found or inactive");
                            return;
                        }

                        var tokenSecurityStamp = context.Principal?.FindFirst("security_stamp")?.Value;
                        var userSecurityStamp = user.SecurityStamp;

                        if (string.IsNullOrEmpty(tokenSecurityStamp) ||
                            string.IsNullOrEmpty(userSecurityStamp) ||
                            tokenSecurityStamp != userSecurityStamp)
                        {
                            context.Fail("Token has been invalidated. Please login again.");
                            return;
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await ApiResults.WriteResultAsync(
                            context.Response,
                            StatusCodes.Status401Unauthorized,
                            Result.Failure(new[] { "غير مصرح." }));
                    },
                    OnForbidden = async context =>
                    {
                        await ApiResults.WriteResultAsync(
                            context.Response,
                            StatusCodes.Status403Forbidden,
                            Result.Failure(new[] { "ليس لديك الصلاحية المطلوبة." }));
                    }
                };
            });

            return services;
        }
    }
}

