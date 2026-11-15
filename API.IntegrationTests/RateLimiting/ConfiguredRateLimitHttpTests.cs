using System.Net;
using API.IntegrationTests.Infrastructure;
using API.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace API.IntegrationTests.RateLimiting;

public sealed class ConfiguredRateLimitNormalUseTests : IClassFixture<ConfiguredRateLimitWebApplicationFactory>
{
    private readonly ConfiguredRateLimitWebApplicationFactory _factory;

    public ConfiguredRateLimitNormalUseTests(ConfiguredRateLimitWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Two_tabs_and_a_busy_general_session_do_not_hit_safety_or_category_limits()
    {
        var settings = Settings();
        var first = _factory.CreateSession();
        var second = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await first.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);

        var page = await Task.WhenAll(
            PageCalls(first),
            PageCalls(second));
        foreach (var response in page.SelectMany(calls => calls))
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            response.Dispose();
        }

        for (var i = 0; i < 80; i++)
        {
            var me = await first.MeAsync();
            Assert.NotEqual(HttpStatusCode.TooManyRequests, me.StatusCode);
            me.Dispose();
        }

        var authenticatedCalls = 14 + 80;
        Assert.True(authenticatedCalls < settings.Safety.Authenticated.TokenLimit);
        Assert.True(82 < settings.Global.Authenticated.TokenLimit);
        Assert.True(6 < settings.Administrative.TokenLimit);
        Assert.True(4 < settings.HeavyRead.TokenLimit);
        Assert.True(2 < settings.ReferenceData.TokenLimit);

        var bound = Settings();
        Assert.Equal(160, bound.Safety.Authenticated.TokenLimit);
        Assert.Equal(24, bound.HeavyRead.TokenLimit);
        Assert.NotEqual(1000, bound.Safety.Authenticated.TokenLimit);
    }

    private RateLimitingSettings Settings()
        => _factory.Services.GetRequiredService<IOptions<RateLimitingSettings>>().Value;

    private static async Task<List<HttpResponseMessage>> PageCalls(AuthHttpSession session)
    {
        var urls = new[]
        {
            "/api/Auth/me",
            "/api/FieldPermissions/GetFieldPermissions",
            "/api/FieldPermissions/GetEntities",
            "/api/FieldPermissions/GetAccessTypes",
            "/api/operational-reports/central/kpis",
            "/api/operational-reports/central/compare",
            "/api/centers"
        };
        var responses = new List<HttpResponseMessage>(urls.Length);
        foreach (var url in urls)
            responses.Add(await session.Client.GetAsync(url));

        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        return responses;
    }
}

public sealed class ConfiguredRateLimitIsolationTests : IClassFixture<ConfiguredRateLimitWebApplicationFactory>
{
    private readonly ConfiguredRateLimitWebApplicationFactory _factory;

    public ConfiguredRateLimitIsolationTests(ConfiguredRateLimitWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Exhausting_heavy_read_does_not_block_field_permission_reads()
    {
        var settings = _factory.Services.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);
        var registry = _factory.Services.GetRequiredService<RateLimitRejectionRegistry>();
        var before = registry.Count(RateLimitPolicies.HeavyRead, RateLimitRejectionReason.CategoryQuota);

        for (var i = 0; i < settings.HeavyRead.TokenLimit; i++)
        {
            var allowed = await session.Client.GetAsync("/api/operational-reports/central/kpis");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
            allowed.Dispose();
        }

        var blocked = await session.Client.GetAsync("/api/operational-reports/central/kpis");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.TryGetValues("Retry-After", out var retry));
        Assert.True(int.TryParse(retry.Single(), out var seconds));
        Assert.True(seconds > 0);
        var body = await blocked.Content.ReadAsStringAsync();
        Assert.Contains("rate_limit_exceeded", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AuthTestSeed.AdminPassword, body, StringComparison.Ordinal);
        blocked.Dispose();

        var fields = await session.Client.GetAsync("/api/FieldPermissions/GetFieldPermissions");
        Assert.Equal(HttpStatusCode.OK, fields.StatusCode);
        fields.Dispose();

        Assert.True(registry.Count(RateLimitPolicies.HeavyRead, RateLimitRejectionReason.CategoryQuota) > before);
        Assert.Equal(0, registry.Count(RateLimitPolicies.Administrative, RateLimitRejectionReason.CategoryQuota));
    }
}

public sealed class ConfiguredRateLimitSafetyTests : IClassFixture<ConfiguredRateLimitWebApplicationFactory>
{
    private readonly ConfiguredRateLimitWebApplicationFactory _factory;

    public ConfiguredRateLimitSafetyTests(ConfiguredRateLimitWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Safety_ceiling_rejects_mixed_groups_while_each_group_still_has_room()
    {
        var settings = _factory.Services.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
        var plan = MixedPlan(settings);
        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);
        var registry = _factory.Services.GetRequiredService<RateLimitRejectionRegistry>();
        var before = registry.Count(RateLimitPolicies.Safety, RateLimitRejectionReason.SafetyCeiling);

        var calls = new List<(string Url, string Group)>();
        calls.AddRange(Enumerable.Repeat(("/api/Auth/me", "general"), plan.General));
        calls.AddRange(Enumerable.Repeat(("/api/FieldPermissions/GetFieldPermissions", "admin"), plan.Admin));
        calls.AddRange(Enumerable.Repeat(("/api/centers", "reference"), plan.Reference));
        calls.AddRange(Enumerable.Repeat(("/api/operational-reports/central/kpis", "heavy"), plan.Heavy));
        Assert.Equal(settings.Safety.Authenticated.TokenLimit + 1, calls.Count);

        HttpResponseMessage? overflow = null;
        for (var i = 0; i < calls.Count; i++)
        {
            var response = await session.Client.GetAsync(calls[i].Url);
            if (i < settings.Safety.Authenticated.TokenLimit)
            {
                Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
                response.Dispose();
            }
            else
            {
                overflow = response;
            }
        }

        Assert.NotNull(overflow);
        Assert.Equal(HttpStatusCode.TooManyRequests, overflow.StatusCode);
        overflow.Dispose();
        Assert.True(registry.Count(RateLimitPolicies.Safety, RateLimitRejectionReason.SafetyCeiling) > before);

        var stillWithinAdmin = await session.Client.GetAsync("/api/FieldPermissions/GetFieldPermissions");
        Assert.Equal(HttpStatusCode.TooManyRequests, stillWithinAdmin.StatusCode);
        stillWithinAdmin.Dispose();
        Assert.True(plan.Admin < settings.Administrative.TokenLimit);
        Assert.True(plan.Heavy < settings.HeavyRead.TokenLimit);
        Assert.True(plan.Reference < settings.ReferenceData.TokenLimit);
        Assert.True(plan.General < settings.Global.Authenticated.TokenLimit);
        Assert.Equal(0, registry.Count(RateLimitPolicies.Administrative, RateLimitRejectionReason.CategoryQuota));
    }

    private static (int General, int Admin, int Reference, int Heavy) MixedPlan(RateLimitingSettings settings)
    {
        var safety = settings.Safety.Authenticated.TokenLimit;
        var general = Math.Min(100, settings.Global.Authenticated.TokenLimit - 1);
        var admin = Math.Min(30, settings.Administrative.TokenLimit - 1);
        var reference = Math.Min(20, settings.ReferenceData.TokenLimit - 1);
        var heavy = safety + 1 - general - admin - reference;
        Assert.True(heavy > 0 && heavy < settings.HeavyRead.TokenLimit);
        var headroom = (settings.Global.Authenticated.TokenLimit - 1)
            + (settings.Administrative.TokenLimit - 1)
            + (settings.ReferenceData.TokenLimit - 1)
            + (settings.HeavyRead.TokenLimit - 1);
        Assert.True(safety < headroom);
        return (general, admin, reference, heavy);
    }
}

public sealed class ConfiguredRateLimitAuthTests : IClassFixture<ConfiguredRateLimitWebApplicationFactory>
{
    private readonly ConfiguredRateLimitWebApplicationFactory _factory;

    public ConfiguredRateLimitAuthTests(ConfiguredRateLimitWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Login_refresh_and_export_keep_their_configured_ceilings()
    {
        var settings = _factory.Services.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
        Assert.Equal(5, settings.Login.PermitLimit);
        Assert.Equal(5, settings.Refresh.PermitLimit);
        Assert.Equal(10, settings.Reports.TokenLimit);
        Assert.Equal(2, settings.Reports.ConcurrencyLimit);

        var login = _factory.CreateSession();
        for (var i = 0; i < settings.Login.PermitLimit; i++)
        {
            var attempt = await login.LoginAsync("rate.limit.real.user", "Wrong!Pass9");
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
            attempt.Dispose();
        }

        var limited = await login.LoginAsync("rate.limit.real.user", "Wrong!Pass9");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.False(limited.Headers.Contains("Retry-After"));
        var loginBody = await limited.Content.ReadAsStringAsync();
        Assert.Contains("rate_limit_exceeded", loginBody, StringComparison.OrdinalIgnoreCase);
        limited.Dispose();

        var registry = _factory.Services.GetRequiredService<RateLimitRejectionRegistry>();
        Assert.True(registry.Count(RateLimitPolicies.Login, RateLimitRejectionReason.AuthWindow) > 0);

        var session = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);
        for (var i = 0; i < settings.Refresh.PermitLimit; i++)
        {
            var refresh = await session.RefreshAsync();
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
            refresh.Dispose();
        }

        var refreshLimited = await session.RefreshAsync();
        Assert.Equal(HttpStatusCode.TooManyRequests, refreshLimited.StatusCode);
        refreshLimited.Dispose();
        Assert.True(registry.Count(RateLimitPolicies.Refresh, RateLimitRejectionReason.AuthWindow) > 0);

        for (var i = 0; i < settings.Reports.TokenLimit; i++)
        {
            var export = await session.Client.GetAsync("/api/operational-reports/central/kpis/export");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, export.StatusCode);
            export.Dispose();
        }

        var exportLimited = await session.Client.GetAsync("/api/operational-reports/central/kpis/export");
        Assert.Equal(HttpStatusCode.TooManyRequests, exportLimited.StatusCode);
        Assert.True(exportLimited.Headers.TryGetValues("Retry-After", out var exportRetry));
        Assert.True(int.TryParse(exportRetry.Single(), out var exportSeconds));
        Assert.True(exportSeconds > 0);
        exportLimited.Dispose();
        Assert.True(registry.Count(RateLimitPolicies.Reports, RateLimitRejectionReason.CategoryQuota) > 0);
    }
}
