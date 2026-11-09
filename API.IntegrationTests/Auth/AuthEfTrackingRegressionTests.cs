using System.Net;
using API.IntegrationTests.Infrastructure;
using Application.Dtos.AuthDtos;
using Application.Interfaces.Services;
using Domain.Entities.Auth;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.IntegrationTests.Auth;

/// <summary>
/// MH-SEC-071 — regression against duplicate EF tracking during real Identity + SQLite host flows.
/// Provider: SQLite file via <see cref="AuthWebApplicationFactory"/> (not InMemory mocks).
/// </summary>
[Collection(AuthHttp091Collection.Name)]
public sealed class AuthEfTrackingRegressionTests
{
    private readonly AuthWebApplicationFactory _factory;

    public AuthEfTrackingRegressionTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    [Trait("Regression", "MH-SEC-071")]
    public async Task Wrong_password_login_increments_counter_in_fresh_scope_without_tracking_errors()
    {
        await ResetLoginCountersAsync(AuthTestSeed.ActiveUserName);

        await RunInScopeAsync(async sp =>
        {
            var signIn = sp.GetRequiredService<SignInManager<User>>();
            var user = await sp.GetRequiredService<UserManager<User>>().FindByNameAsync(AuthTestSeed.ActiveUserName);
            Assert.NotNull(user);
            var result = await signIn.CheckPasswordSignInAsync(user!, "Wrong!Pass9", lockoutOnFailure: true);
            Assert.False(result.Succeeded);
        });

        var count = await ReadAccessFailedCountAsync(AuthTestSeed.ActiveUserName);
        Assert.True(count >= 1);
    }

    [Fact]
    [Trait("Regression", "MH-SEC-071")]
    public async Task Concurrent_scoped_password_checks_do_not_throw_duplicate_tracking()
    {
        await ResetLoginCountersAsync(AuthTestSeed.ActiveUserName);
        var exceptions = new List<Exception>();

        var tasks = Enumerable.Range(0, 6).Select(async _ =>
        {
            try
            {
                await RunInScopeAsync(async sp =>
                {
                    var users = sp.GetRequiredService<UserManager<User>>();
                    var signIn = sp.GetRequiredService<SignInManager<User>>();
                    var user = await users.FindByNameAsync(AuthTestSeed.ActiveUserName);
                    Assert.NotNull(user);
                    await signIn.CheckPasswordSignInAsync(user!, "Wrong!Pass9", lockoutOnFailure: true);
                });
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        });
        await Task.WhenAll(tasks);

        AssertNoTrackingNoise(exceptions);
    }

    [Fact]
    [Trait("Regression", "MH-SEC-071")]
    public async Task Http_login_refresh_logout_flow_has_no_tracking_errors()
    {
        await ResetLoginCountersAsync(AuthTestSeed.ActiveUserName);
        var errors = new List<Exception>();
        try
        {
            var session = _factory.CreateSession();
            Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await session.RefreshAsync()).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await session.LogoutAsync()).StatusCode);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }

        AssertNoTrackingNoise(errors);
    }

    [Fact]
    [Trait("Regression", "MH-SEC-071")]
    public async Task NoTracking_read_then_identity_update_in_same_scope_does_not_duplicate_track()
    {
        await RunInScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var users = sp.GetRequiredService<UserManager<User>>();
            var id = await db.Users.AsNoTracking()
                .Where(u => u.UserName == AuthTestSeed.ActiveUserName)
                .Select(u => u.Id)
                .SingleAsync();
            var tracked = await users.FindByIdAsync(id);
            Assert.NotNull(tracked);
            var stamp = tracked!.SecurityStamp;
            var rotated = await users.UpdateSecurityStampAsync(tracked);
            Assert.True(rotated.Succeeded);
            Assert.NotEqual(stamp, tracked.SecurityStamp);
        });
    }

    [Fact]
    [Trait("Regression", "MH-SEC-071")]
    public async Task Bulk_refresh_revoke_after_disable_uses_execute_update_without_stale_user()
    {
        var victimId = await _factory.WithDbAsync(async db =>
            await db.Users.Where(u => u.UserName == AuthTestSeed.SecondUserName).Select(u => u.Id).SingleAsync());

        var victim = _factory.CreateSession();
        var admin = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await victim.LoginAsync(AuthTestSeed.SecondUserName, AuthTestSeed.SecondPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchUserStatusAsync(victimId, false)).StatusCode);

        var revokedCount = await _factory.WithDbAsync(async db =>
            await db.UserRefreshTokens.CountAsync(t => t.UserId == victimId && t.IsRevoked));
        Assert.True(revokedCount >= 1);

        var stamp = await _factory.WithDbAsync(async db =>
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == victimId)).SecurityStamp);
        Assert.False(string.IsNullOrWhiteSpace(stamp));

        await admin.PatchUserStatusAsync(victimId, true);
    }

    private async Task RunInScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    private async Task<int> ReadAccessFailedCountAsync(string userName)
        => await _factory.WithDbAsync(async db =>
            (await db.Users.AsNoTracking().SingleAsync(u => u.UserName == userName)).AccessFailedCount);

    private async Task ResetLoginCountersAsync(string userName)
    {
        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.UserName == userName);
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            user.LoginLockoutTier = 0;
            await db.SaveChangesAsync();
        });
    }

    private static void AssertNoTrackingNoise(IReadOnlyCollection<Exception> exceptions)
    {
        foreach (var ex in exceptions)
        {
            var text = ex.ToString();
            Assert.DoesNotContain("another instance with the same key", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("already being tracked", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cannot be tracked", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Empty(exceptions);
    }
}
