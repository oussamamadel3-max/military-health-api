using API.Infrastructure;
using Application.Common.Results;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace API.Middleware
{
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache; // نستخدم IMemoryCache لتجنب استنزاف الذاكرة.
        private const string CacheKeyPrefix = "RateLimit_";

        public RateLimitingMiddleware(RequestDelegate next, IMemoryCache cache)
        {
            _next = next;
            _cache = cache;
        }

        // إعدادات Token Bucket
        private static readonly TokenBucketRateLimiterOptions LimiterOptions = new()
        {
            TokenLimit = 40, // الحد الأقصى للانفجار
            TokensPerPeriod = 10, // تجديد 10 توكنات
            ReplenishmentPeriod = TimeSpan.FromSeconds(10), // كل 10 ثوانٍ (للحفاظ على معدل 60/دقيقة)
            AutoReplenishment = true,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0 // الرفض الفوري هو الأفضل للـ API
        };

        // فترة انتهاء صلاحية محدد السرعة (Limiter) في الذاكرة: 15 دقيقة
        // إذا لم يتم استخدام IP خلال هذه المدة، يتم إزالته من الذاكرة.
        private static readonly MemoryCacheEntryOptions CacheOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
        };

        public async Task InvokeAsync(HttpContext context)
        {
            // ✅ لا تقيّد طلبات CORS preflight
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                await _next(context);
                return;
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var cacheKey = CacheKeyPrefix + ip;

            var limiter = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.SetOptions(CacheOptions);
                return new TokenBucketRateLimiter(LimiterOptions);
            });

            using var lease = await limiter!.AcquireAsync(1);

            if (lease.IsAcquired)
            {
                context.Response.Headers["X-RateLimit-Limit"] = LimiterOptions.TokenLimit.ToString();
                await _next(context);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json; charset=utf-8";

                const string rawMessage = "لقد تجاوزت الحد المسموح به من الطلبات. يرجى المحاولة بعد قليل.";
                context.Response.Headers["X-RateLimit-Error-Message"] = WebUtility.UrlEncode(rawMessage);

                if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                    context.Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();

                var response = Result.Failure(new List<string> { rawMessage });
                var json = JsonSerializer.Serialize(response, ApiJsonSerializerOptions.CamelCase);

                await context.Response.WriteAsync(json);
            }
        }

        //public async Task InvokeAsync(HttpContext context)
        //{
        //    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        //    var cacheKey = CacheKeyPrefix + ip;

        //    // 1. استرجاع أو إنشاء محدد السرعة (Limiter) من IMemoryCache
        //    var limiter = _cache.GetOrCreate(cacheKey, entry =>
        //    {
        //        entry.SetOptions(CacheOptions);
        //        return new TokenBucketRateLimiter(LimiterOptions);
        //    });

        //    // 2. محاولة حجز رمز واحد لكل طلب
        //    using var lease = await limiter!.AcquireAsync(1); // استخدام using لضمان التخلص من المورد

        //    if (lease.IsAcquired)
        //    {
        //        // ✅ الطلب مسموح به
        //        context.Response.Headers.Append("X-RateLimit-Limit", LimiterOptions.TokenLimit.ToString());

        //        await _next(context);
        //    }
        //    else
        //    {
        //        // 🚫 تجاوز الحد – إرجاع 429 و Headers مفيدة
        //        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        //        context.Response.ContentType = "application/json; charset=utf-8";

        //        const string rawMessage = "لقد تجاوزت الحد المسموح به من الطلبات. يرجى المحاولة بعد قليل.";

        //        // 🌟 التعديل: تشفير الرسالة قبل إضافتها إلى Header
        //        var encodedMessage = WebUtility.UrlEncode(rawMessage);
        //        context.Response.Headers.Append("X-RateLimit-Error-Message", encodedMessage);

        //        // استخدام GetMetadata لاسترداد قيمة RetryAfter
        //        if (lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        //        {
        //            var retryAfterSeconds = (int)retryAfter.TotalSeconds;
        //            context.Response.Headers.Append("Retry-After", retryAfterSeconds.ToString());
        //        }

        //        // لا يزال من الجيد إرجاع JSON Body لمن يستخدمون أدوات خارجية (مثل Postman)
        //        var response = Result.Failure(new List<string> { rawMessage });

        //        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        //        {
        //            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        //        });

        //        await context.Response.WriteAsync(json);
        //    }
        //}
    }
}
