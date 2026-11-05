using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace API.RateLimiting;

public static class RateLimitingExtensions
{
    public const string RateLimitExceededMessage =
        "لقد تجاوزت الحد المسموح به من الطلبات. يرجى المحاولة بعد قليل.";

    public const string RateLimitExceededCode = "rate_limit_exceeded";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<RateLimitingSettings>, RateLimitingSettingsValidator>();
        services.AddOptions<RateLimitingSettings>()
            .Bind(configuration.GetSection(RateLimitingSettings.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>, ForwardedHeadersSettingsValidator>();
        services.AddOptions<ForwardedHeadersSettings>()
            .Bind(configuration.GetSection(ForwardedHeadersSettings.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IRateLimitPartitionKeyResolver, RateLimitPartitionKeyResolver>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = WriteRateLimitRejectedAsync;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var resolver = httpContext.RequestServices.GetRequiredService<IRateLimitPartitionKeyResolver>();
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                var key = resolver.ResolveUserOrIp(httpContext);
                var bucket = key.StartsWith("user:", StringComparison.Ordinal)
                    ? settings.Global.Authenticated
                    : settings.Global.Anonymous;

                return RateLimitPartition.GetTokenBucketLimiter(key, _ => CreateTokenBucket(bucket));
            });

            options.AddPolicy(RateLimitPolicies.Login, httpContext =>
            {
                var resolver = httpContext.RequestServices.GetRequiredService<IRateLimitPartitionKeyResolver>();
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                var key = resolver.ResolveIp(httpContext);

                return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = settings.Login.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.Login.WindowSeconds),
                    SegmentsPerWindow = settings.Login.SegmentsPerWindow,
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });

            // MH-SEC-055 / D3: IP-only sliding window (same shape as Login). In-memory counters are
            // per process — multi-instance deployments do not share budgets (sticky sessions / per-node
            // capacity must be considered; Redis is out of scope).
            options.AddPolicy(RateLimitPolicies.Refresh, httpContext =>
            {
                var resolver = httpContext.RequestServices.GetRequiredService<IRateLimitPartitionKeyResolver>();
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                var key = resolver.ResolveIp(httpContext);

                return RateLimitPartition.GetSlidingWindowLimiter(
                    $"refresh:{key}",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = settings.Refresh.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.Refresh.WindowSeconds),
                        SegmentsPerWindow = settings.Refresh.SegmentsPerWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            options.AddPolicy(RateLimitPolicies.Reports, httpContext =>
            {
                var resolver = httpContext.RequestServices.GetRequiredService<IRateLimitPartitionKeyResolver>();
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                var key = resolver.ResolveUserOrIp(httpContext);

                return RateLimitPartition.Get(key, _ => new SequentialRateLimiter(
                    new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                    {
                        PermitLimit = settings.Reports.ConcurrencyLimit,
                        QueueLimit = 0
                    }),
                    new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = settings.Reports.TokenLimit,
                        TokensPerPeriod = settings.Reports.TokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(settings.Reports.ReplenishmentPeriodSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    })));
            });

            options.AddPolicy(RateLimitPolicies.PublicApi, httpContext =>
            {
                var resolver = httpContext.RequestServices.GetRequiredService<IRateLimitPartitionKeyResolver>();
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                var key = resolver.ResolveIp(httpContext);

                return RateLimitPartition.GetTokenBucketLimiter(key, _ => CreateTokenBucket(settings.Public));
            });
        });

        return services;
    }

    public static IApplicationBuilder UseAppForwardedHeaders(this IApplicationBuilder app)
    {
        var settings = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };

        // Empty lists: keep ASP.NET Core defaults (loopback trusted). Do NOT Clear().
        foreach (var proxy in settings.KnownProxies)
        {
            if (IPAddress.TryParse(proxy.Trim(), out var address))
                options.KnownProxies.Add(address);
        }

        foreach (var network in settings.KnownIPNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network.Trim(), out var ipNetwork))
                options.KnownIPNetworks.Add(ipNetwork);
        }

        return app.UseForwardedHeaders(options);
    }

    private static TokenBucketRateLimiterOptions CreateTokenBucket(TokenBucketSettings settings) => new()
    {
        TokenLimit = settings.TokenLimit,
        TokensPerPeriod = settings.TokensPerPeriod,
        ReplenishmentPeriod = TimeSpan.FromSeconds(settings.ReplenishmentPeriodSeconds),
        QueueLimit = 0,
        AutoReplenishment = true
    };

    private static TokenBucketRateLimiterOptions CreateTokenBucket(PublicRateLimitSettings settings) => new()
    {
        TokenLimit = settings.TokenLimit,
        TokensPerPeriod = settings.TokensPerPeriod,
        ReplenishmentPeriod = TimeSpan.FromSeconds(settings.ReplenishmentPeriodSeconds),
        QueueLimit = 0,
        AutoReplenishment = true
    };

    private static async ValueTask WriteRateLimitRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var logger = httpContext.RequestServices.GetService<ILoggerFactory>()
            ?.CreateLogger("API.RateLimiting");

        var endpoint = httpContext.GetEndpoint()?.DisplayName;
        var endpointPolicy = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        var authenticated = httpContext.User?.Identity?.IsAuthenticated == true;

        logger?.LogWarning(
            "Rate limit rejected. Endpoint={Endpoint} EndpointPolicy={EndpointPolicy} Authenticated={Authenticated} StatusCode={StatusCode}",
            endpoint,
            endpointPolicy,
            authenticated,
            StatusCodes.Status429TooManyRequests);

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        httpContext.Response.Headers["X-RateLimit-Error-Message"] = WebUtility.UrlEncode(RateLimitExceededMessage);

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            httpContext.Response.Headers["Retry-After"] = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        var payload = new
        {
            success = false,
            error = new
            {
                code = RateLimitExceededCode,
                message = RateLimitExceededMessage,
                meta = new { }
            }
        };

        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken);
    }
}
