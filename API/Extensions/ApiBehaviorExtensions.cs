using Application.Common.Results;
using Microsoft.AspNetCore.Mvc;

namespace API.Extensions
{
    public static class ApiBehaviorExtensions
    {
        public static IServiceCollection AddApiValidationBehavior(this IServiceCollection services)
        {
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                    new BadRequestObjectResult(context.ModelState.ToFailureResult());
            });

            return services;
        }
    }
}
