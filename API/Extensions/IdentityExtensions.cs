using Application.Interfaces.Services;
using Application.Options;
using Domain.Entities.Auth;
using Infrastructure.Persistence;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace API.Extensions
{
    public static class IdentityExtensions
    {
        public static IServiceCollection AddCustomIdentity(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                var currentUser = sp.GetRequiredService<ICurrentUserService>();
                options.UseNpgsql(connectionString);
                //options.UseSqlServer(connectionString);
            });

            services.AddSingleton<IValidateOptions<LoginLockoutSettings>, LoginLockoutSettingsValidator>();
            services.AddOptions<LoginLockoutSettings>()
                .Bind(configuration.GetSection(LoginLockoutSettings.SectionName))
                .ValidateOnStart();

            var lockoutSettings = configuration
                .GetSection(LoginLockoutSettings.SectionName)
                .Get<LoginLockoutSettings>() ?? new LoginLockoutSettings();

            var firstDurationSeconds = lockoutSettings.DurationsSeconds is { Length: > 0 }
                ? lockoutSettings.DurationsSeconds[0]
                : 30;

            services.AddIdentity<User, Role>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = Math.Max(1, lockoutSettings.MaxFailedAccessAttempts);
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromSeconds(Math.Max(1, firstDurationSeconds));
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddErrorDescriber<ArabicIdentityErrorDescriber>()
            .AddDefaultTokenProviders();

            return services;
        }
    }
}
