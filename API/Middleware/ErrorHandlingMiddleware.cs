using API.Infrastructure;
using Domain.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Net;

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

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        IResult response;

        switch (exception)
        {
            // =========================================================================
            // 1. Custom Business Exceptions (Domain & Application Logic Errors)
            // =========================================================================
            case BusinessException bizEx:
                response = ApiResponse.Error(
                    message: bizEx.Message,
                    code: bizEx.ErrorCode,
                    meta: bizEx.Meta,
                    status: bizEx.Status);

                _logger.LogWarning(
                    "BUSINESS EXCEPTION: Code={ErrorCode}, Status={Status}, Message={Message}",
                    bizEx.ErrorCode, bizEx.Status, bizEx.Message);
                break;

            // =========================================================================
            // 2. Unauthorized Access Exception
            // =========================================================================
            case UnauthorizedAccessException:
                response = ApiResponse.Error(
                    message: "غير مسموح بالوصول لهذا المورد.",
                    code: "UNAUTHORIZED_ACCESS",
                    status: (int)HttpStatusCode.Unauthorized);
                break;

            // =========================================================================
            // 3. Resource Not Found Exception
            // =========================================================================
            case KeyNotFoundException:
                response = ApiResponse.Error(
                    message: "المورد المطلوب غير موجود.",
                    code: "RESOURCE_NOT_FOUND",
                    status: (int)HttpStatusCode.NotFound);
                break;

            // =========================================================================
            // 4. BadImageFormatException (Preserved system error handler)
            // =========================================================================
            case BadImageFormatException:
                response = ApiResponse.Error(
                    message: "حدث خطأ فني في إعدادات الخادم. يرجى إبلاغ فريق الدعم الفني فوراً.",
                    code: "SERVER_CONFIG_ERROR",
                    status: (int)HttpStatusCode.InternalServerError);
                break;

            // =========================================================================
            // 5. Default Fallback (Unexpected System Server Errors - 500)
            // =========================================================================
            default:
                var friendlyMessage = "عذراً، حدث خطأ غير متوقع أثناء معالجة طلبك. (يرجى الاتصال بالدعم)";
                var meta = _environment.IsDevelopment() ? new { originalError = exception.Message } : null;

                response = ApiResponse.Error(
                    message: friendlyMessage,
                    code: "UNEXPECTED_SERVER_ERROR",
                    meta: meta,
                    status: (int)HttpStatusCode.InternalServerError);

                _logger.LogError(exception,
                    "UNHANDLED SYSTEM EXCEPTION: Message={Message}", exception.Message);
                break;
        }

        await response.ExecuteAsync(context);
    }
}

public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ErrorHandlingMiddleware>();
    }
}
