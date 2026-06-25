using API.Infrastructure;
using Application.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace API.Extensions;

/// <summary>
/// استجابات HTTP موحّدة بصيغة <see cref="Result"/> لكل مسارات الأخطاء.
/// </summary>
public static class ApiResults
{
    public static UnauthorizedObjectResult Unauthorized(IEnumerable<string> errors)
        => new(Result.Failure(errors));

    public static UnauthorizedObjectResult Unauthorized(string error)
        => Unauthorized(new[] { error });

    public static ObjectResult Forbidden(IEnumerable<string> errors)
        => new(Result.Failure(errors)) { StatusCode = StatusCodes.Status403Forbidden };

    public static ObjectResult Forbidden(string error)
        => Forbidden(new[] { error });

    public static BadRequestObjectResult BadRequest(IEnumerable<string> errors)
        => new(Result.Failure(errors));

    public static BadRequestObjectResult BadRequest(string error)
        => BadRequest(new[] { error });

    public static NotFoundObjectResult NotFound(IEnumerable<string> errors)
        => new(Result.Failure(errors));

    public static NotFoundObjectResult NotFound(string error)
        => NotFound(new[] { error });

    public static Task WriteResultAsync(
        HttpResponse response,
        int statusCode,
        Result result,
        CancellationToken cancellationToken = default)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        return response.WriteAsJsonAsync(result, ApiJsonSerializerOptions.CamelCase, cancellationToken);
    }
}