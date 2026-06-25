using Application.Common.Results;

namespace API.Extensions;

internal static class HttpResponseResultExtensions
{
    public static Task WriteResultAsync(
        this HttpResponse response,
        int statusCode,
        Result result,
        CancellationToken cancellationToken = default)
        => ApiResults.WriteResultAsync(response, statusCode, result, cancellationToken);
}
