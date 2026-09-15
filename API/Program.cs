using API.Extensions;
using API.Middleware;
using API.RateLimiting;
using Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

#region ================== CONFIGURE SERVICES ==================

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddHttpContextAccessor();

System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

builder.Services
    .AddApplicationAndInfrastructure()
    .AddCustomIdentity(builder.Configuration)
    .AddJwtAuthentication(builder.Configuration)
    .AddSwaggerDocumentation(builder.Environment)
    .AddFrontendCors(builder.Configuration)
    .AddAppRateLimiting(builder.Configuration);

builder.Services.AddCookieSecurity(builder.Environment);
builder.Services.AddApiValidationBehavior();
builder.Services.AddHealthChecks();

#endregion

var app = builder.Build();

#region ================== MIDDLEWARE PIPELINE ==================

app.UseSecurityHeaders();

// Single ForwardedHeaders call (X-Forwarded-For | X-Forwarded-Proto).
// Empty KnownProxies/KnownIPNetworks keep ASP.NET defaults (loopback trusted).
app.UseAppForwardedHeaders();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseStaticFiles();
app.UseErrorHandling();
app.UseRouting();
app.UseSwaggerDocumentation(app.Environment);
app.UseFrontendCors();
app.UseHttpsRedirection();
app.UseCookieSecurity();

// Authentication BEFORE RateLimiter so Global can partition by UserId.
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<CsrfValidationMiddleware>();
app.UseAuthorization();
app.UseMiddleware<CurrentUserMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health").DisableRateLimiting();

#endregion

#region ================== DATABASE SEED ==================

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

// Expose for integration tests that host the real Program if needed later.
public partial class Program { }
