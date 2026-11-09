using System.Net;
using API.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace API.IntegrationTests.Auth;

/// <summary>MH-SEC-040 / MH-SEC-090-001: admin disables a user with live session families.</summary>
[Collection(AuthHttp091Collection.Name)]
public sealed class AuthDisableActiveSessionHttpTests
{
    private readonly AuthWebApplicationFactory _factory;

    public AuthDisableActiveSessionHttpTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Fact]
    [Trait("Regression", "MH-SEC-040")]
    public async Task Disable_active_user_invalidates_both_sessions_other_user_unaffected()
    {
        await EnsureUserActiveAsync(AuthTestSeed.SecondUserName, true);
        await EnsureUserActiveAsync(AuthTestSeed.ActiveUserName, true);
        await EnsureUserActiveAsync(AuthTestSeed.AdminUserName, true);
        await RestoreKnownPasswordAsync(AuthTestSeed.SecondUserName, AuthTestSeed.SecondPassword);

        var victimId = await _factory.WithDbAsync(async db =>
            await db.Users.AsNoTracking()
                .Where(u => u.UserName == AuthTestSeed.SecondUserName)
                .Select(u => u.Id)
                .SingleAsync());

        var session1 = _factory.CreateSession();
        var session2 = _factory.CreateSession();
        var otherUser = _factory.CreateSession();
        var admin = _factory.CreateSession();

        Assert.Equal(HttpStatusCode.OK, (await session1.LoginAsync(AuthTestSeed.SecondUserName, AuthTestSeed.SecondPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session2.LoginAsync(AuthTestSeed.SecondUserName, AuthTestSeed.SecondPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherUser.LoginAsync(AuthTestSeed.ActiveUserName, AuthTestSeed.ActivePassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);

        var fullNameBefore = await _factory.WithDbAsync(async db =>
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == victimId)).FullName);

        var disable = await admin.PatchUserStatusAsync(victimId, isActive: false);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        await admin.AssertNoSecretsInBodyAsync(disable, AuthTestSeed.SecondPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, (await session1.MeAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session2.RefreshAsync()).StatusCode);

        var write = await session2.UpdateMyProfileAsync("محاولة تعديل", "blocked@it.local", "EMP-BLOCK", "محظور");
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);

        var fullNameAfter = await _factory.WithDbAsync(async db =>
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == victimId)).FullName);
        Assert.Equal(fullNameBefore, fullNameAfter);

        Assert.Equal(HttpStatusCode.Unauthorized, (await session1.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session2.MeAsync()).StatusCode);

        var enable = await admin.PatchUserStatusAsync(victimId, isActive: true);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await session1.MeAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session2.RefreshAsync()).StatusCode);

        var fresh = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await fresh.LoginAsync(AuthTestSeed.SecondUserName, AuthTestSeed.SecondPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await otherUser.MeAsync()).StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            var events = await db.Set<Domain.Entities.Audit.AuditEvent>()
                .AsNoTracking()
                .OrderByDescending(e => e.EventId)
                .Take(30)
                .ToListAsync();
            Assert.Contains(events, e => e.EventType.Contains("Disable", StringComparison.OrdinalIgnoreCase)
                                       || (e.ReasonText ?? string.Empty).Contains("تعطيل", StringComparison.Ordinal));
            foreach (var evt in events)
            {
                Assert.DoesNotContain(AuthTestSeed.SecondPassword, evt.ReasonText ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain("RefreshToken", evt.NewValues ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        });

        await EnsureUserActiveAsync(AuthTestSeed.SecondUserName, true);
        await RestoreKnownPasswordAsync(AuthTestSeed.SecondUserName, AuthTestSeed.SecondPassword);
    }

    private async Task RestoreKnownPasswordAsync(string userName, string password)
    {
        var userId = await _factory.WithDbAsync(async db =>
            await db.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync());
        var admin = _factory.CreateSession();
        if ((await admin.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode != HttpStatusCode.OK)
            return;
        await admin.AdminResetPasswordAsync(userId, password);
    }

    private async Task EnsureUserActiveAsync(string userName, bool isActive)
    {
        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.UserName == userName);
            user.IsActive = isActive;
            if (isActive)
            {
                user.LockoutEnd = null;
                user.AccessFailedCount = 0;
                user.LoginLockoutTier = 0;
            }
            await db.SaveChangesAsync();
        });
    }
}
