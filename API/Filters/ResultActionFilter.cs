using Application.Common.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Filters;

/// <summary>
/// يربط <see cref="Result"/> / <see cref="Result{T}"/> المرجَع من Controllers بـ HTTP status مناسب.
/// الفشل → 400 (التمييز الدقيق 404/401/403 يُضاف في المرحلة 2 مع ErrorDetail).
/// </summary>
public sealed class ResultActionFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult objectResult
            && objectResult.Value is Result result
            && !result.Succeeded
            && IsImplicitSuccessStatus(objectResult.StatusCode))
        {
            objectResult.StatusCode = StatusCodes.Status400BadRequest;
        }

        return next();
    }

    private static bool IsImplicitSuccessStatus(int? statusCode)
        => statusCode is null or StatusCodes.Status200OK;
}
