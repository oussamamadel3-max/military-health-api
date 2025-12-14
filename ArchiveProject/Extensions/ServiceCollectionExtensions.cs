using ArchiveProject.Application;
using ArchiveProject.Infrastructure;

namespace ArchiveProject.API.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationAndInfrastructure(this IServiceCollection services)
        {
            services.ConfigureApplicationServices();
            services.ConfigureInfrastructureServices();


            return services;
        }
    }
}

