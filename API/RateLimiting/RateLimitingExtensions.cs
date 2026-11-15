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
        services.AddSingleton<RateLimitRejectionRegistry>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRateLimitRejectedAsync;

            // Order: concurrency (heavy reads and exports only), then the general bucket
            // (unclassified endpoints only), then the shared safety ceiling.
            // A concurrency rejection stops the chain before any token bucket is acquired.
            // GetNoLimiter is only the inactive stage. The safety ceiling still runs for every classified request.
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    var policy = PolicyName(httpContext);
                    var key = UserOrIp(httpContext);
                    var settings = SettingsOf(httpContext);

                    if (policy == RateLimitPolicies.Reports)
                        return ConcurrencyPartition(key, policy, settings.Reports.ConcurrencyLimit);
                    if (policy == RateLimitPolicies.HeavyRead)
                        return ConcurrencyPartition(key, policy, settings.HeavyRead.ConcurrencyLimit);

                    return RateLimitPartition.GetNoLimiter("concurrency:none");
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    if (!string.IsNullOrEmpty(PolicyName(httpContext)))
                        return RateLimitPartition.GetNoLimiter("general:none");

                    var key = UserOrIp(httpContext);
                    var settings = SettingsOf(httpContext);
                    var bucket = IsAuthenticatedKey(key)
                        ? settings.Global.Authenticated
                        : settings.Global.Anonymous;

                    return RateLimitPartition.Get(
                        "general:" + key,
                        _ => new ReasonedRateLimiter(
                            new TokenBucketRateLimiter(CreateTokenBucket(bucket)),
                            RateLimitPolicies.General,
                            RateLimitRejectionReason.CategoryQuota));
                }),
                PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    var key = UserOrIp(httpContext);
                    var settings = SettingsOf(httpContext);
                    var bucket = IsAuthenticatedKey(key)
                        ? settings.Safety.Authenticated
                        : settings.Safety.Anonymous;

                    return RateLimitPartition.Get(
                        "safety:" + key,
                        _ => new ReasonedRateLimiter(
                            new TokenBucketRateLimiter(CreateTokenBucket(bucket)),
                            RateLimitPolicies.Safety,
                            RateLimitRejectionReason.SafetyCeiling));
                }));

            options.AddPolicy(RateLimitPolicies.Login, httpContext =>
            {
                var resolver = httpContext.RequestServices.GetRequiredService<IRateLimitPartitionKeyResolver>();
                var settings = SettingsOf(httpContext);
                var key = resolver.ResolveIp(httpContext);

                return RateLimitPartition.Get(key, _ => new ReasonedRateLimiter(
                    new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = settings.Login.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.Login.WindowSeconds),
                        SegmentsPerWindow = settings.Login.SegmentsPerWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }),
                    RateLimitPolicies.Login,
                    RateLimitRejectionReason.AuthWindow));
            });

            // MH-SEC-055 / D3: IP-only sliding window (same shape as Login). In-memory counters are
            // per process — multi-instance deployments do not share budgets (sticky sessions / per-node
            // capacity must be considered; Redis is out of scope).
            options.AddPolicy(RateLimitPolicies.Refresh, httpContext =>
            {
                var resolver = httpContext.RequestServices.GetRequiredService<IRateLimitPartitionKeyResolver>();
                var settings = SettingsOf(httpContext);
                var key = resolver.ResolveIp(httpContext);

                return RateLimitPartition.Get(
                    "refresh:" + key,
                    _ => new ReasonedRateLimiter(
                        new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = settings.Refresh.PermitLimit,
                            Window = TimeSpan.FromSeconds(settings.Refresh.WindowSeconds),
                            SegmentsPerWindow = settings.Refresh.SegmentsPerWindow,
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }),
                        RateLimitPolicies.Refresh,
                        RateLimitRejectionReason.AuthWindow));
            });

            // Token bucket only. Concurrency is the first stage of GlobalLimiter so a concurrency
            // rejection does not consume safety or export tokens.
            options.AddPolicy(RateLimitPolicies.Reports, httpContext =>
            {
                var key = UserOrIp(httpContext);
                var settings = SettingsOf(httpContext);
                return CategoryTokenPartition(key, RateLimitPolicies.Reports, settings.Reports);
            });

            options.AddPolicy(RateLimitPolicies.HeavyRead, httpContext =>
            {
                var key = UserOrIp(httpContext);
                var settings = SettingsOf(httpContext);
                return CategoryTokenPartition(key, RateLimitPolicies.HeavyRead, settings.HeavyRead);
            });

            options.AddPolicy(RateLimitPolicies.Administrative, httpContext =>
            {
                var key = UserOrIp(httpContext);
                var settings = SettingsOf(httpContext);
                return CategoryTokenPartition(key, RateLimitPolicies.Administrative, settings.Administrative);
            });

            options.AddPolicy(RateLimitPolicies.ReferenceData, httpContext =>
            {
                var key = UserOrIp(httpContext);
                var settings = SettingsOf(httpContext);
                return CategoryTokenPartition(key, RateLimitPolicies.ReferenceData, settings.ReferenceData);
            });

            options.AddPolicy(RateLimitPolicies.PublicApi, httpContext =>
            {
                var resolver = httpContext.RequestServices.GetRequiredService<IRateLimitPartitionKeyResolver>();
                var settings = SettingsOf(httpContext);
                var key = resolver.ResolveIp(httpContext);

                return RateLimitPartition.Get(
                    "public:" + key,
                    _ => new ReasonedRateLimiter(
                        new TokenBucketRateLimiter(CreateTokenBucket(settings.Public)),
                        RateLimitPolicies.PublicApi,
                        RateLimitRejectionReason.CategoryQuota));
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

    private static RateLimitPartition<string> ConcurrencyPartition(string key, string policy, int permitLimit)
        => RateLimitPartition.Get(
            "concurrency:" + policy + ":" + key,
            _ => new ReasonedRateLimiter(
                new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                {
                    PermitLimit = permitLimit,
                    QueueLimit = 0
                }),
                policy,
                RateLimitRejectionReason.Concurrency));

    private static RateLimitPartition<string> CategoryTokenPartition(string key, string policy, ReportsRateLimitSettings settings)
        => RateLimitPartition.Get(
            policy + ":" + key,
            _ => new ReasonedRateLimiter(
                new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
                {
                    TokenLimit = settings.TokenLimit,
                    TokensPerPeriod = settings.TokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(settings.ReplenishmentPeriodSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }),
                policy,
                RateLimitRejectionReason.CategoryQuota));

    private static RateLimitPartition<string> CategoryTokenPartition(string key, string policy, TokenBucketSettings settings)
        => RateLimitPartition.Get(
            policy + ":" + key,
            _ => new ReasonedRateLimiter(
                new TokenBucketRateLimiter(CreateTokenBucket(settings)),
                policy,
                RateLimitRejectionReason.CategoryQuota));

    private static RateLimitingSettings SettingsOf(HttpContext httpContext)
        => httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;

    private static string UserOrIp(HttpContext httpContext)
        => httpContext.RequestServices.GetRequiredService<IRateLimitPartitionKeyResolver>().ResolveUserOrIp(httpContext);

    private static string? PolicyName(HttpContext httpContext)
        => httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    private static bool IsAuthenticatedKey(string key)
        => key.StartsWith("user:", StringComparison.Ordinal);

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
        var registry = httpContext.RequestServices.GetService<RateLimitRejectionRegistry>();

        var endpoint = httpContext.GetEndpoint()?.DisplayName;
        var policy = "unknown";
        var reason = "unspecified";
        if (context.Lease.TryGetMetadata(RateLimitMetadataNames.Policy, out object? policyMetadata)
            && policyMetadata is string policyName
            && !string.IsNullOrWhiteSpace(policyName))
            policy = policyName;
        if (context.Lease.TryGetMetadata(RateLimitMetadataNames.Reason, out object? reasonMetadata)
            && reasonMetadata is string reasonName
            && !string.IsNullOrWhiteSpace(reasonName))
            reason = reasonName;

        registry?.Increment(policy, reason);

        int? retryAfterSeconds = null;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);

        logger?.LogWarning(
            "Rate limit rejected. Endpoint={Endpoint} Policy={Policy} Reason={Reason} RetryAfterSeconds={RetryAfterSeconds} RejectionCounts={RejectionCounts}",
            endpoint,
            policy,
            reason,
            retryAfterSeconds,
            registry?.FormatSnapshot());

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        httpContext.Response.Headers["X-RateLimit-Error-Message"] = WebUtility.UrlEncode(RateLimitExceededMessage);

        if (retryAfterSeconds is int seconds)
            httpContext.Response.Headers["Retry-After"] = seconds.ToString();

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
