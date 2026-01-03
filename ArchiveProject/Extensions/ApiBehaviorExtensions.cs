using Microsoft.AspNetCore.Mvc;

namespace ArchiveProject.API.Extensions
{
    public static class ApiBehaviorExtensions
    {
        public static IServiceCollection AddApiValidationBehavior(this IServiceCollection services)
        {
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(kvp => kvp.Value?.Errors.Count > 0)
                        .SelectMany(kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage))
                        .ToList();

                    return new BadRequestObjectResult(new
                    {
                        message = "بيانات غير صالحة",
                        errors
                    });
                }; 
            });

            return services;
        }
    }
}
