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
                        .GroupBy(kvp => string.IsNullOrEmpty(kvp.Key) ? "$" : kvp.Key)
                        .ToDictionary(
                            g => g.Key,
                            g => g.SelectMany(x => x.Value!.Errors
                                    .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "قيمة غير صالحة." : e.ErrorMessage))
                                .ToArray());

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
