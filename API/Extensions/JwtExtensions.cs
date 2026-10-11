using API.Infrastructure;
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
using Application.Common.Security;
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
                        var tokenSecurityStamp = context.Principal?.FindFirst("security_stamp")?.Value;
                        var userSecurityStamp = user?.SecurityStamp;

                        if (!AccessTokenSessionGate.IsAccessTokenAccepted(
                                userExists: user != null,
                                isActive: user?.IsActive ?? false,
                                tokenSecurityStamp: tokenSecurityStamp,
                                userSecurityStamp: userSecurityStamp))
                        {
                            context.Fail(user == null || !user.IsActive
                                ? "User not found or inactive"
                                : "Token has been invalidated. Please login again.");
                            return;
                        }

                        var sessionFamily = context.Principal?.FindFirst(SessionFamilyGate.ClaimType)?.Value;
                        var refreshTokens = context.HttpContext.RequestServices.GetRequiredService<IRefreshTokenRepository>();
                        var familyActive = Guid.TryParse(sessionFamily, out var familyId)
                            && await refreshTokens.HasActiveInFamilyAsync(familyId, DateTime.UtcNow);
                        if (!SessionFamilyGate.AllowsAccess(sessionFamily, familyActive))
                        {
                            context.Fail("Session ended");
                            return;
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await ApiResponse.Error("غير مصرح.", "UNAUTHORIZED_ACCESS", status: 401)
                                         .ExecuteAsync(context.HttpContext);
                    },
                    OnForbidden = async context =>
                    {
                        await ApiResponse.Error("ليس لديك الصلاحية المطلوبة.", "FORBIDDEN_ACCESS", status: 403)
                                         .ExecuteAsync(context.HttpContext);
                    }
                };
            });

            return services;
        }
    }
}

