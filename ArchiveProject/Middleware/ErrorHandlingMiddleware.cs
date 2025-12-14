using ArchiveProject.Application.Common.Results;
using System.Net;
using System.Text.Json;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
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

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // 1. تهيئة المتغيرات
        var statusCode = HttpStatusCode.InternalServerError; // الافتراضي 500
        var errors = new List<string>();
        var warnings = new List<string>();
        string originalErrorMessage = exception.Message;

        // 2. معالجة وتحديد كود الحالة والرسائل
        switch (exception)
        {

            case BadImageFormatException:
                errors.Add("حدث خطأ فني في إعدادات الخادم. يرجى إبلاغ فريق الدعم الفني فوراً.");
                statusCode = HttpStatusCode.InternalServerError;
                break;

            default:
                // رسالة صديقة عامة لـ 500
                errors.Add("عذراً، حدث خطأ غير متوقع أثناء معالجة طلبك. (يرجى الاتصال بالدعم)");
                statusCode = HttpStatusCode.InternalServerError;
                break;
        }

        // 3. التسجيل والتسليم (التحسين الرئيسي المطلوب)

        // 3.1. تسجيل الخطأ الأصلي كـ Warning (يراه المطور)
        _logger.LogWarning(exception,
                           "UNHANDLED WARNING (Original): {OriginalMessage}. HTTP Status: {StatusCode}",
                           originalErrorMessage, (int)statusCode);

        // 3.2. تسجيل الرسالة البسيطة كـ Error (يراه فريق التشغيل)
        _logger.LogError("CLIENT ERROR: Returning status {StatusCode} with message: {FriendlyMessage}",
                         (int)statusCode, errors.FirstOrDefault() ?? "No specific error message.");


        // 4. إعداد الاستجابة للعميل

        // 💡 إضافة الرسالة الأصلية إلى قائمة التحذيرات في الاستجابة (لتراها الواجهة الأمامية)
        if (statusCode == HttpStatusCode.InternalServerError)
        {
            // نرسل الخطأ الأصلي كتحذير فقط في حالة 500 (الأخطاء غير المتوقعة)
            warnings.Add($"Original Error: {originalErrorMessage}");
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        // 💡 استخدام Result.Failure مع Errors و Warnings
        var errorResponse = Result.Failure(errors, warnings: warnings);

        // 5. كتابة الاستجابة
        return context.Response.WriteAsJsonAsync(errorResponse, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}

// Extension Method لتسهيل استخدام الطبقة الوسيطة في Program.cs
public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ErrorHandlingMiddleware>();
    }
}
