using API.Infrastructure;
using Application.Exceptions;
using Application.Interfaces.Services;
using Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace API.Middleware;

/// <summary>
/// معالجة مركزية للأخطاء: رسالة آمنة للعميل مع معرّف تتبع، وتفاصيل كاملة في السجل الداخلي فقط (FR-079).
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
        var correlationId = ResolveCorrelationId(context);
        if (!context.Response.HasStarted)
            context.Response.Headers["X-Correlation-Id"] = correlationId.ToString("D");

        var safeMeta = new { correlationId };
        IResult response;

        switch (exception)
        {
            // =========================================================================
            // 1. Custom Business Exceptions (Domain & Application Logic Errors)
            // =========================================================================
            case UserAccountLockedException lockedEx:
                if (!context.Response.HasStarted)
                    context.Response.Headers["Retry-After"] = lockedEx.RetryAfterSeconds.ToString();

                response = ApiResponse.Error(
                    message: lockedEx.Message,
                    code: lockedEx.ErrorCode,
                    meta: MergeMeta(lockedEx.Meta, correlationId),
                    status: lockedEx.Status);

                _logger.LogWarning(
                    "LOGIN LOCKOUT: CorrelationId={CorrelationId}, Code={ErrorCode}, RetryAfter={RetryAfter}, Message={Message}",
                    correlationId, lockedEx.ErrorCode, lockedEx.RetryAfterSeconds, lockedEx.Message);
                break;

            case BusinessException bizEx:
                response = ApiResponse.Error(
                    message: bizEx.Message,
                    code: bizEx.ErrorCode,
                    meta: MergeMeta(bizEx.Meta, correlationId),
                    status: bizEx.Status);

                _logger.LogWarning(
                    "BUSINESS EXCEPTION: CorrelationId={CorrelationId}, Code={ErrorCode}, Status={Status}, Message={Message}",
                    correlationId, bizEx.ErrorCode, bizEx.Status, bizEx.Message);
                break;

            // =========================================================================
            // 2. Unauthorized Access Exception
            // =========================================================================
            case UnauthorizedAccessException:
                response = ApiResponse.Error(
                    message: "غير مسموح بالوصول لهذا المورد.",
                    code: "UNAUTHORIZED_ACCESS",
                    meta: safeMeta,
                    status: (int)HttpStatusCode.Unauthorized);
                break;

            // =========================================================================
            // 3. Resource Not Found Exception
            // =========================================================================
            case KeyNotFoundException:
                response = ApiResponse.Error(
                    message: "المورد المطلوب غير موجود.",
                    code: "RESOURCE_NOT_FOUND",
                    meta: safeMeta,
                    status: (int)HttpStatusCode.NotFound);
                break;

            // =========================================================================
            // 4. Argument & Invalid Operation Exceptions (Input / State Validation)
            // =========================================================================
            case ArgumentException argEx:
                response = ApiResponse.Error(
                    message: "المدخلات غير صالحة أو مفقودة.",
                    code: "INVALID_ARGUMENT",
                    meta: safeMeta,
                    status: (int)HttpStatusCode.BadRequest);

                _logger.LogWarning(argEx,
                    "ARGUMENT EXCEPTION: CorrelationId={CorrelationId}, Message={Message}",
                    correlationId, argEx.Message);
                break;

            case InvalidOperationException invOpEx:
                response = ApiResponse.Error(
                    message: "تعذر تنفيذ العملية في الحالة الحالية.",
                    code: "INVALID_OPERATION",
                    meta: safeMeta,
                    status: (int)HttpStatusCode.BadRequest);

                _logger.LogWarning(invOpEx,
                    "INVALID OPERATION EXCEPTION: CorrelationId={CorrelationId}, Message={Message}",
                    correlationId, invOpEx.Message);
                break;

            // =========================================================================
            // 5. Database Update Exceptions
            // =========================================================================
            case DbUpdateException dbEx when IsUniqueConstraintViolation(dbEx):
                response = ApiResponse.Error(
                    message: "تعارض في البيانات: القيمة موجودة مسبقاً.",
                    code: "DATABASE_CONFLICT",
                    meta: safeMeta,
                    status: (int)HttpStatusCode.Conflict);

                _logger.LogWarning(dbEx,
                    "DATABASE CONFLICT: CorrelationId={CorrelationId}, Message={Message}",
                    correlationId, dbEx.Message);
                break;

            case DbUpdateException dbEx:
                response = ApiResponse.Error(
                    message: "حدث خطأ أثناء حفظ البيانات في قاعدة البيانات.",
                    code: "DATABASE_ERROR",
                    meta: safeMeta,
                    status: (int)HttpStatusCode.InternalServerError);

                _logger.LogError(dbEx,
                    "DATABASE UPDATE EXCEPTION: CorrelationId={CorrelationId}, Message={Message}",
                    correlationId, dbEx.Message);
                break;

            // =========================================================================
            // 6. BadImageFormatException (Preserved system error handler)
            // =========================================================================
            case BadImageFormatException:
                response = ApiResponse.Error(
                    message: "حدث خطأ فني في إعدادات الخادم. يرجى إبلاغ فريق الدعم الفني فوراً.",
                    code: "SERVER_CONFIG_ERROR",
                    meta: safeMeta,
                    status: (int)HttpStatusCode.InternalServerError);
                break;

            // =========================================================================
            // 7. Default Fallback (Unexpected System Server Errors - 500)
            // =========================================================================
            default:
                // FR-079: لا تُعاد تفاصيل تقنية/Stack/SQL للعميل حتى في Development.
                response = ApiResponse.Error(
                    message: "عذراً، حدث خطأ غير متوقع أثناء معالجة طلبك. استخدم معرّف التتبع عند التواصل مع الدعم.",
                    code: "UNEXPECTED_SERVER_ERROR",
                    meta: safeMeta,
                    status: (int)HttpStatusCode.InternalServerError);

                _logger.LogError(exception,
                    "UNHANDLED SYSTEM EXCEPTION: CorrelationId={CorrelationId}, Message={Message}",
                    correlationId, exception.Message);
                break;
        }

        await response.ExecuteAsync(context);
    }

    private static Guid ResolveCorrelationId(HttpContext context)
    {
        var currentUser = context.RequestServices.GetService<ICurrentUserService>();
        if (currentUser is not null && currentUser.CorrelationId != Guid.Empty)
            return currentUser.CorrelationId;

        var header = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        if (Guid.TryParse(header, out var fromHeader) && fromHeader != Guid.Empty)
            return fromHeader;

        return Guid.NewGuid();
    }

    private static object MergeMeta(object? existing, Guid correlationId)
    {
        if (existing is null)
            return new { correlationId };

        return new { correlationId, detail = existing };
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
            return sqlEx.Number == 2601 || sqlEx.Number == 2627;

        if (ex.InnerException is Npgsql.PostgresException pgEx)
            return pgEx.SqlState == "23505";

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
