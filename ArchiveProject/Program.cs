using ArchiveProject.API.Extensions;
using ArchiveProject.API.Middleware;
using ArchiveProject.Infrastructure.Persistence;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

#region ✅ إعداد الخدمات (Service Configuration)

// 🧩 إضافة الـ Controllers التي تحتوي على الـ Endpoints الخاصة بـ API
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // استخدام camelCase للأسماء
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        // إظهار القيم null في الاستجابة JSON (لإظهار الحقول حتى لو كانت null)
    });
builder.Services.AddHttpContextAccessor();

// 🧠 إضافة كاش داخلي بالذاكرة لتحسين الأداء وتخزين البيانات المؤقتة
builder.Services.AddMemoryCache();

// 🚫 منع الـ JWT من تعديل أسماء الـ Claims (حتى لا يتم تحويلها تلقائيًا مثل "sub" → "nameidentifier")
System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

// ⚙️ تسجيل الخدمات والطبقات المختلفة للنظام (Application, Infrastructure, Identity, JWT, Swagger, CORS)
builder.Services
    // 🧱 ربط طبقة التطبيق (Application Layer) والبنية التحتية (Infrastructure Layer)
    .AddApplicationAndInfrastructure()

    // 👤 إعداد نظام الهوية (Identity) وربطه بقاعدة البيانات
    .AddCustomIdentity(builder.Configuration)

    // 🔐 تفعيل التوثيق باستخدام JWT Token
    .AddJwtAuthentication(builder.Configuration)

    // 📘 إعداد واجهة التوثيق Swagger لتجربة الـ API
    .AddSwaggerDocumentation()

    // 🌐 إعداد سياسة CORS للسماح بتواصل الواجهة الأمامية (Angular أو غيره)
    .AddFrontendCors(builder.Configuration);

#endregion

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context =>
    {
        // الحصول على IP
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,                 // عدد المحاولات
                Window = TimeSpan.FromMinutes(1), // خلال دقيقة
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});
builder.Services.AddCookieSecurity();
builder.Services.AddApiValidationBehavior();


var app = builder.Build();
// 🔐 Security Headers (Clickjacking protection)
app.UseSecurityHeaders();

app.UseStaticFiles(); // يقدّم كل ما في wwwroot على /
app.UseRateLimiter();



// في السطر 82 قم باضافة الصلاحيات الخاصة بك في ملف DbInitializer

#region  تنفيذ Seed قاعدة البيانات عند التشغيل

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        // Seed قاعدة البيانات
        await DbInitializer.SeedAsync(services);
        Console.WriteLine("✅ Database seeding completed successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error during database seeding: {ex.Message}");
        throw; // يمكن حذف هذا السطر إذا أردت الاستمرار حتى عند الفشل
    }
}
#endregion


#region 🧭 خط سير التنفيذ (Middleware Pipeline)

// ⚠️ تفعيل الـ Middleware الخاص بالتعامل مع الأخطاء العامة في التطبيق
app.UseErrorHandling();

// ⏳ Middleware مخصص لتحديد عدد الطلبات المسموحة (Rate Limiting)
app.UseMiddleware<RateLimitingMiddleware>();

// 🌍 تفعيل سياسة الـ CORS للسماح للفرونت إند بالاتصال بالـ API
app.UseFrontendCors();

// 🔒 تفعيل التحويل التلقائي إلى HTTPS
app.UseHttpsRedirection();

// 🍪 تفعيل إعدادات أمان الكوكيز
app.UseCookieSecurity();

// 🔑 تفعيل نظام المصادقة (Authentication) للتحقق من هوية المستخدمين
app.UseAuthentication();

// 🧾 تفعيل نظام التفويض (Authorization) للتحكم بالصلاحيات بعد التوثيق
app.UseAuthorization();

// 👤 Middleware مخصص للحصول على المستخدم الحالي وتخزينه في السياق
app.UseMiddleware<CurrentUserMiddleware>();

// 📘 تفعيل Swagger لتوثيق الـ API وتوفير واجهة تجربة التوكنات والطلبات
app.UseSwaggerDocumentation();

// 📡 ربط جميع الـ Controllers بالـ Endpoints الخاصة بها
app.MapControllers();

#endregion


app.Run();
