using API.Infrastructure;
using API.Validation;
using Microsoft.AspNetCore.Mvc;

namespace API.Extensions;

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
                    .ToDictionary(
                        kvp => string.IsNullOrEmpty(kvp.Key) ? "general" : char.ToLowerInvariant(kvp.Key[0]) + kvp.Key[1..],
                        kvp => kvp.Value!.Errors.Select(e => ValidationErrorText.Localize(kvp.Key, e.ErrorMessage)).ToArray()
                    );

                return new UnprocessableEntityObjectResult(new
                {
                    success = false,
                    error = new
                    {
                        code = "VALIDATION_ERROR",
                        message = ValidationErrorText.EnvelopeMessage,
                        meta = new { errors }
                    }
                });
            };
        });

        return services;
    }
}
