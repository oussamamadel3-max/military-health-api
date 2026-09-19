using API.Infrastructure;
using Application.Exceptions;
using Domain.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
            case UserAccountLockedException lockedEx:
                if (!context.Response.HasStarted)
                {
                    context.Response.Headers["Retry-After"] = lockedEx.RetryAfterSeconds.ToString();
                }

                response = ApiResponse.Error(
                    message: lockedEx.Message,
                    code: lockedEx.ErrorCode,
                    meta: lockedEx.Meta,
                    status: lockedEx.Status);

                _logger.LogWarning(
                    "LOGIN LOCKOUT: Code={ErrorCode}, RetryAfter={RetryAfter}, Message={Message}",
                    lockedEx.ErrorCode, lockedEx.RetryAfterSeconds, lockedEx.Message);
                break;

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
            // 4. Argument & Invalid Operation Exceptions (Input / State Validation)
            // =========================================================================
            case ArgumentException argEx:
                response = ApiResponse.Error(
                    message: "المدخلات غير صالحة أو مفقودة.",
                    code: "INVALID_ARGUMENT",
                    status: (int)HttpStatusCode.BadRequest);

                _logger.LogWarning(argEx, "ARGUMENT EXCEPTION: Message={Message}", argEx.Message);
                break;

            case InvalidOperationException invOpEx:
                response = ApiResponse.Error(
                    message: "تعذر تنفيذ العملية في الحالة الحالية.",
                    code: "INVALID_OPERATION",
                    status: (int)HttpStatusCode.BadRequest);

                _logger.LogWarning(invOpEx, "INVALID OPERATION EXCEPTION: Message={Message}", invOpEx.Message);
                break;

            // =========================================================================
            // 5. Database Update Exceptions
            // =========================================================================
            case DbUpdateException dbEx when IsUniqueConstraintViolation(dbEx):
                response = ApiResponse.Error(
                    message: "تعارض في البيانات: القيمة موجودة مسبقاً.",
                    code: "DATABASE_CONFLICT",
                    status: (int)HttpStatusCode.Conflict);

                _logger.LogWarning(dbEx, "DATABASE CONFLICT: Message={Message}", dbEx.Message);
                break;

            case DbUpdateException dbEx:
                response = ApiResponse.Error(
                    message: "حدث خطأ أثناء حفظ البيانات في قاعدة البيانات.",
                    code: "DATABASE_ERROR",
                    status: (int)HttpStatusCode.InternalServerError);

                _logger.LogError(dbEx, "DATABASE UPDATE EXCEPTION: Message={Message}", dbEx.Message);
                break;

            // =========================================================================
            // 6. BadImageFormatException (Preserved system error handler)
            // =========================================================================
            case BadImageFormatException:
                response = ApiResponse.Error(
                    message: "حدث خطأ فني في إعدادات الخادم. يرجى إبلاغ فريق الدعم الفني فوراً.",
                    code: "SERVER_CONFIG_ERROR",
                    status: (int)HttpStatusCode.InternalServerError);
                break;

            // =========================================================================
            // 7. Default Fallback (Unexpected System Server Errors - 500)
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
            return sqlEx.Number == 2601 || sqlEx.Number == 2627;
        return false;
    }
}

public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ErrorHandlingMiddleware>();
    }
}
