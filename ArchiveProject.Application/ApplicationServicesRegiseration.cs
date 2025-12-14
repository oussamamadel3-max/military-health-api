using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace ArchiveProject.Application
{
    public static class ApplicationServicesRegiseration
    {
        public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services)
        {
            services.AddAutoMapper(Assembly.GetExecutingAssembly());

            return services;
        }
    }
}

