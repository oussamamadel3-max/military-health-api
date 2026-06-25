using Application.Common.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace API.Extensions;

internal static class ModelStateExtensions
{
    /// <summary>
    /// يحوّل أخطاء ModelState إلى Result.Failure بصيغة "field: message".
    /// </summary>
    public static Result ToFailureResult(this ModelStateDictionary modelState)
    {
        var errors = modelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .SelectMany(kvp =>
            {
                var field = string.IsNullOrEmpty(kvp.Key) ? "$" : kvp.Key;
                return kvp.Value!.Errors.Select(e =>
                {
                    var message = string.IsNullOrEmpty(e.ErrorMessage)
                        ? "قيمة غير صالحة."
                        : e.ErrorMessage;
                    return $"{field}: {message}";
                });
            })
            .ToArray();

        return Result.Failure(errors);
    }
}
