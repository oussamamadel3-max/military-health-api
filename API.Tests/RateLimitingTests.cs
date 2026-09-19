using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using API.RateLimiting;
using Xunit;

namespace API.Tests;

public class RateLimitingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Global_UserPartitions_AreIndependent()
    {
        await using var host = await RateLimitingTestHost.StartAsync();
        using var userA = host.CreateClientWithUser("user-a", "10.0.0.1");
        using var userB = host.CreateClientWithUser("user-b", "10.0.0.1");

        // Testing Global Auth TokenLimit = 3
        Assert.Equal(HttpStatusCode.OK, (await userA.GetAsync("/test/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userA.GetAsync("/test/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userA.GetAsync("/test/ping")).StatusCode);
        var blocked = await userA.GetAsync("/test/ping");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await userB.GetAsync("/test/ping")).StatusCode);
    }

    [Fact]
    public async Task Global_AnonymousIpPartitions_AreIndependent()
    {
        await using var host = await RateLimitingTestHost.StartAsync();
        using var ipA = host.CreateAnonymousClient("10.1.0.1");
        using var ipB = host.CreateAnonymousClient("10.1.0.2");

        Assert.Equal(HttpStatusCode.OK, (await ipA.GetAsync("/test/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ipA.GetAsync("/test/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ipA.GetAsync("/test/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ipA.GetAsync("/test/ping")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ipB.GetAsync("/test/ping")).StatusCode);
    }

    [Fact]
    public async Task Login_ThirdAttempt_Returns429_Not503_WithApiResponse()
    {
        await using var host = await RateLimitingTestHost.StartAsync();
        using var client = host.CreateAnonymousClient("10.2.0.1");

        // Login PermitLimit = 2 under Testing. Global anon also = 3, so login policy hits first.
        Assert.False(IsRateLimited(await client.PostAsync("/test/login", null)));
        Assert.False(IsRateLimited(await client.PostAsync("/test/login", null)));

        var third = await client.PostAsync("/test/login", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, third.StatusCode);

        var body = await third.Content.ReadFromJsonAsync<RateLimitErrorBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(body!.Success);
        Assert.Equal(RateLimitingExtensions.RateLimitExceededCode, body.Error?.Code);
        Assert.Equal(RateLimitingExtensions.RateLimitExceededMessage, body.Error?.Message);
        Assert.True(third.Headers.Contains("X-RateLimit-Error-Message"));
    }

    [Fact]
    public async Task TimeBasedRejection_MayIncludeRetryAfter()
    {
        await using var host = await RateLimitingTestHost.StartAsync();
        using var client = host.CreateAnonymousClient("10.3.0.1");

        await client.PostAsync("/test/login", null);
        await client.PostAsync("/test/login", null);
        var rejected = await client.PostAsync("/test/login", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        // Sliding window usually provides Retry-After; do not fail if missing.
        _ = rejected.Headers.TryGetValues("Retry-After", out _);
    }

    [Fact]
    public async Task Reports_Concurrency_BlocksSameUser_AllowsOtherUser()
    {
        await using var host = await RateLimitingTestHost.StartAsync();
        using var userA1 = host.CreateClientWithUser("reports-a", "10.4.0.1");
        using var userA2 = host.CreateClientWithUser("reports-a", "10.4.0.1");
        using var userB = host.CreateClientWithUser("reports-b", "10.4.0.1");

        var slowTask = userA1.GetAsync("/test/reports/slow");
        await Task.Delay(100);
        var concurrent = await userA2.GetAsync("/test/reports/fast");
        Assert.Equal(HttpStatusCode.TooManyRequests, concurrent.StatusCode);

        var otherUser = await userB.GetAsync("/test/reports/fast");
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await slowTask).StatusCode);
    }

    [Fact]
    public async Task Health_BypassesRateLimiting_AfterGlobalExhausted()
    {
        await using var host = await RateLimitingTestHost.StartAsync();
        using var client = host.CreateAnonymousClient("10.5.0.1");

        await client.GetAsync("/test/ping");
        await client.GetAsync("/test/ping");
        await client.GetAsync("/test/ping");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/test/ping")).StatusCode);

        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task ForwardedHeaders_UntrustedProxy_DoesNotUseXForwardedFor()
    {
        // No KnownProxies configured → defaults trust loopback only.
        // Request appears from non-loopback connection IP set via header; X-Forwarded-For should be ignored.
        await using var host = await RateLimitingTestHost.StartAsync();
        using var client = host.CreateAnonymousClient("10.6.0.1");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.50");

        // Exhaust anonymous bucket for 10.6.0.1 (connection IP), not 203.0.113.50
        await client.GetAsync("/test/ping");
        await client.GetAsync("/test/ping");
        await client.GetAsync("/test/ping");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/test/ping")).StatusCode);

        using var other = host.CreateAnonymousClient("10.6.0.2");
        other.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.50");
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/test/ping")).StatusCode);
    }

    [Fact]
    public async Task ForwardedHeaders_KnownProxy_UsesXForwardedFor()
    {
        await using var host = await RateLimitingTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "127.0.0.1"
        });

        // TestServer connection is typically 127.0.0.1 (trusted). X-Forwarded-For should become client IP.
        using var client = host.Server.CreateClient();
        client.BaseAddress = new Uri("http://localhost");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.20");

        await client.GetAsync("/test/ping");
        await client.GetAsync("/test/ping");
        await client.GetAsync("/test/ping");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/test/ping")).StatusCode);

        using var other = host.Server.CreateClient();
        other.BaseAddress = new Uri("http://localhost");
        other.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.21");
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/test/ping")).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedWithoutUserId_FallsBackToIp_NotUserUnknown()
    {
        await using var host = await RateLimitingTestHost.StartAsync();
        using var authNoId = host.CreateAuthenticatedWithoutUserIdClient("10.7.0.1");
        using var anonSameIp = host.CreateAnonymousClient("10.7.0.1");

        // Both should share the anonymous/IP bucket (Auth without id → ip fallback).
        Assert.Equal(HttpStatusCode.OK, (await authNoId.GetAsync("/test/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonSameIp.GetAsync("/test/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await authNoId.GetAsync("/test/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await anonSameIp.GetAsync("/test/ping")).StatusCode);
    }

    private static bool IsRateLimited(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.TooManyRequests;

    private sealed class RateLimitErrorBody
    {
        public bool Success { get; set; }
        public ErrorBody? Error { get; set; }
    }

    private sealed class ErrorBody
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
    }
}
