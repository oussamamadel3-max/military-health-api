using System.Security.Claims;
using System.Text.Encodings.Web;
using API.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace API.Tests;

/// <summary>
/// Minimal host for Rate Limiting tests. Does not boot PostgreSQL or the full Program pipeline.
/// Forces Environment=Testing and loads appsettings.Testing.json.
/// </summary>
public sealed class RateLimitingTestHost : IAsyncDisposable
{
    public const string TestAuthScheme = "TestAuth";

    private readonly WebApplication _app;

    public HttpClient Client { get; }
    public TestServer Server { get; }

    private RateLimitingTestHost(WebApplication app, HttpClient client, TestServer server)
    {
        _app = app;
        Client = client;
        Server = server;
    }

    public static async Task<RateLimitingTestHost> StartAsync(
        IEnumerable<KeyValuePair<string, string?>>? extraConfig = null)
    {
        var testingSettings = ResolveTestingSettingsPath();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Environment.EnvironmentName = "Testing";
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(testingSettings, optional: false, reloadOnChange: false);
        if (extraConfig is not null)
            builder.Configuration.AddInMemoryCollection(extraConfig);

        builder.Services.AddLogging(l => l.ClearProviders());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAppRateLimiting(builder.Configuration);

        builder.Services.AddAuthentication(TestAuthScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthScheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddHealthChecks();

        builder.WebHost.UseTestServer();

        var app = builder.Build();

        app.UseAppForwardedHeaders();
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapGet("/test/ping", () => Results.Ok(new { ok = true }));
        app.MapPost("/test/login", () => Results.Ok(new { ok = true }))
            .RequireRateLimiting(RateLimitPolicies.Login);
        app.MapGet("/test/reports/slow", async () =>
            {
                await Task.Delay(1500);
                return Results.Ok(new { ok = true });
            })
            .RequireRateLimiting(RateLimitPolicies.Reports);
        app.MapGet("/test/reports/fast", () => Results.Ok(new { ok = true }))
            .RequireRateLimiting(RateLimitPolicies.Reports);
        app.MapHealthChecks("/health").DisableRateLimiting();

        await app.StartAsync();

        var server = app.GetTestServer();
        var client = server.CreateClient();
        client.BaseAddress = new Uri("http://localhost");

        return new RateLimitingTestHost(app, client, server);
    }

    private static string ResolveTestingSettingsPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "appsettings.Testing.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "API", "appsettings.Testing.json"))
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("appsettings.Testing.json was not found for RateLimitingTestHost.");
    }

    public HttpClient CreateClientWithUser(string userId, string? remoteIp = null)
    {
        var client = Server.CreateClient();
        client.BaseAddress = new Uri("http://localhost");
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        if (!string.IsNullOrWhiteSpace(remoteIp))
            client.DefaultRequestHeaders.Add(TestAuthHandler.RemoteIpHeader, remoteIp);
        return client;
    }

    public HttpClient CreateAnonymousClient(string? remoteIp = null)
    {
        var client = Server.CreateClient();
        client.BaseAddress = new Uri("http://localhost");
        if (!string.IsNullOrWhiteSpace(remoteIp))
            client.DefaultRequestHeaders.Add(TestAuthHandler.RemoteIpHeader, remoteIp);
        return client;
    }

    public HttpClient CreateAuthenticatedWithoutUserIdClient(string? remoteIp = null)
    {
        var client = Server.CreateClient();
        client.BaseAddress = new Uri("http://localhost");
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthenticatedWithoutIdHeader, "1");
        if (!string.IsNullOrWhiteSpace(remoteIp))
            client.DefaultRequestHeaders.Add(TestAuthHandler.RemoteIpHeader, remoteIp);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string UserIdHeader = "X-Test-UserId";
    public const string RemoteIpHeader = "X-Test-RemoteIp";
    public const string AuthenticatedWithoutIdHeader = "X-Test-Auth-No-Id";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.TryGetValue(RemoteIpHeader, out var ip) &&
            System.Net.IPAddress.TryParse(ip.ToString(), out var address))
        {
            Context.Connection.RemoteIpAddress = address;
        }

        if (Request.Headers.TryGetValue(AuthenticatedWithoutIdHeader, out var flag) &&
            string.Equals(flag.ToString(), "1", StringComparison.Ordinal))
        {
            var identityNoId = new ClaimsIdentity(authenticationType: RateLimitingTestHost.TestAuthScheme);
            identityNoId.AddClaim(new Claim("role", "tester"));
            var principalNoId = new ClaimsPrincipal(identityNoId);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principalNoId, RateLimitingTestHost.TestAuthScheme)));
        }

        if (!Request.Headers.TryGetValue(UserIdHeader, out var userId) ||
            string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, RateLimitingTestHost.TestAuthScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, RateLimitingTestHost.TestAuthScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
