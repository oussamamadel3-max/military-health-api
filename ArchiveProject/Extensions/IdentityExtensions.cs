using ArchiveProject.Application.Common.Interfaces.AuthInterfaces;
using ArchiveProject.Domain.Entities.Auth;
using ArchiveProject.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArchiveProject.API.Extensions
{
    public static class IdentityExtensions
    {
        public static IServiceCollection AddCustomIdentity(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<MainDbContext>((sp, options) =>
            {
                var currentUser = sp.GetRequiredService<ICurrentUserService>();
                options.UseSqlServer(connectionString);
            });

            services.AddIdentity<User, Role>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;
            })
            .AddEntityFrameworkStores<MainDbContext>()
            .AddDefaultTokenProviders();

            return services;
        }
    }
}

