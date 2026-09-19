using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Application
{
    public static class ApplicationServicesRegiseration
    {
        public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services)
        {
            var config = new TypeAdapterConfig();
            config.Scan(Assembly.GetExecutingAssembly());

            // يفشل الإقلاع فورًا إذا كان أي تحويل غير صالح، بدل أن يظهر الخطأ عند أول طلب
            config.Compile();

            services.AddSingleton(config);
            services.AddScoped<IMapper, ServiceMapper>();

            return services;
        }
    }
}
