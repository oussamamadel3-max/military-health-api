using Application.Common.Results;
using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Text.Json;

namespace API.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var statusCode = HttpStatusCode.InternalServerError;
        var errors = new List<string>();
        var warnings = new List<string>();
        var originalErrorMessage = exception.Message;

        switch (exception)
        {
            case BadImageFormatException:
                errors.Add("حدث خطأ فني في إعدادات الخادم. يرجى إبلاغ فريق الدعم الفني فوراً.");
                statusCode = HttpStatusCode.InternalServerError;
                break;

            default:
                errors.Add("عذراً، حدث خطأ غير متوقع أثناء معالجة طلبك. (يرجى الاتصال بالدعم)");
                statusCode = HttpStatusCode.InternalServerError;
                break;
        }

        _logger.LogWarning(exception,
            "UNHANDLED WARNING (Original): {OriginalMessage}. HTTP Status: {StatusCode}",
            originalErrorMessage, (int)statusCode);

        _logger.LogError("CLIENT ERROR: Returning status {StatusCode} with message: {FriendlyMessage}",
            (int)statusCode, errors.FirstOrDefault() ?? "No specific error message.");

        if (statusCode == HttpStatusCode.InternalServerError && _environment.IsDevelopment())
            warnings.Add($"Original Error: {originalErrorMessage}");

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var errorResponse = Result.Failure(errors, warnings: warnings);

        return context.Response.WriteAsJsonAsync(errorResponse,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}

public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ErrorHandlingMiddleware>();
    }
}
