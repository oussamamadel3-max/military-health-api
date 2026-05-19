using ArchiveProject.API.Extensions;
using ArchiveProject.API.Middleware;
using ArchiveProject.API.Services;
using ArchiveProject.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

#region ================== CONFIGURE SERVICES ==================

/*
 * إضافة Controllers الخاصة بالـ API
 * + ضبط JSON ليكون camelCase (مهم لتوافق Angular)
 */
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

/*
 * تمكين الوصول إلى HttpContext من أي Service
 * (مستخدم في FileUrl, CurrentUser, Audit, ...إلخ)
 */
builder.Services.AddHttpContextAccessor();

/*
 * كاش داخلي بالذاكرة
 * مستخدم في RateLimitingMiddleware
 */
builder.Services.AddMemoryCache();

/*
 * منع ASP.NET من تغيير أسماء الـ JWT Claims تلقائيًا
 * حتى تبقى مثل ما أصدرتها أنت (sub, role, permissions...)
 */
System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

/*
 * تسجيل جميع طبقات المشروع:
 * - Application
 * - Infrastructure
 * - Identity
 * - JWT Authentication
 * - Swagger
 * - CORS
 */
builder.Services
    .AddApplicationAndInfrastructure()
    .AddCustomIdentity(builder.Configuration)
    .AddJwtAuthentication(builder.Configuration)
    .AddSwaggerDocumentation(builder.Environment)
    .AddFrontendCors(builder.Configuration);

/*
 * Rate Limiter مبني داخل ASP.NET
 * (مستخدم فقط لبعض الـ Endpoints مثل login)
 */
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,                  // عدد المحاولات
                Window = TimeSpan.FromMinutes(1), // خلال دقيقة
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

/*
 * Middleware مخصص لتأمين الكوكيز
 * (SameSite, Secure, HttpOnly...)
 */
builder.Services.AddCookieSecurity(builder.Environment);
builder.Services.AddScoped<AuthCookieService>();
builder.Services.AddScoped<ICsrfService, CsrfService>();

/*
 * Middleware مخصص لتوحيد رسائل Validation
 * بدل ما يرجع ASP.NET أخطاء افتراضية
 */
builder.Services.AddApiValidationBehavior();

#endregion

var app = builder.Build();

#region ================== MIDDLEWARE PIPELINE ==================

/*
 * إضافة Security Headers
 * (X-Frame-Options, X-Content-Type-Options, ...)
 */
app.UseSecurityHeaders();

/*
 * مهم جدًا على السيرفر (Reverse Proxy / Nginx / IIS)
 * حتى يتم قراءة IP الحقيقي للمستخدم
 */
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
    app.UseHsts();

/*
 * تقديم الملفات الثابتة من wwwroot
 * (لو عندك صور عامة أو ملفات ثابتة)
 */
app.UseStaticFiles();

/*
 * Middleware مركزي لمعالجة الأخطاء
 * يمنع تسريب StackTrace للعميل
 */
app.UseErrorHandling();

/*
 * تفعيل نظام الـ Routing
 * (ضروري قبل CORS و Authorization)
 */
app.UseRouting();

/*
 * تفعيل سياسة CORS
 * للسماح فقط للـ Frontend المحدد بالوصول
 */
app.UseFrontendCors();

/*
 * تفعيل Rate Limiter الداخلي
 */
app.UseRateLimiter();

/*
 * Rate Limiting مخصص (Token Bucket)
 * لحماية الـ API من Flood / DoS
 */
app.UseMiddleware<RateLimitingMiddleware>();

/*
 * تحويل HTTP → HTTPS
 */
app.UseHttpsRedirection();

/*
 * تفعيل إعدادات أمان الكوكيز
 */
app.UseCookieSecurity();

/*
 * تفعيل المصادقة JWT
 * (قراءة التوكن والتحقق منه)
 */
app.UseAuthentication();

app.UseMiddleware<CsrfValidationMiddleware>();

/*
 * تفعيل التفويض
 * (التحقق من الصلاحيات)
 */
app.UseAuthorization();

/*
 * Middleware مخصص لاستخراج المستخدم الحالي
 * وتخزينه في Context
 */
app.UseMiddleware<CurrentUserMiddleware>();

/*
 * تفعيل Swagger (في التطوير أو حسب الإعداد)
 */
app.UseSwaggerDocumentation(app.Environment);

/*
 * ربط Controllers بالـ Endpoints
 */
app.MapControllers();

#endregion

#region ================== DATABASE SEED ==================

/*
 * تهيئة البيانات الأولية:
 * - Roles
 * - Permissions
 * - Admin User
 * - FieldPermissions
 */
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await DbInitializer.SeedAsync(services);
        Console.WriteLine("✅ Database seeding completed successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error during database seeding: {ex.Message}");
        throw;
    }
}

#endregion

app.Run();
