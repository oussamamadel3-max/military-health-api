using System.Net;
using API.IntegrationTests.Infrastructure;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace API.IntegrationTests.Auth;

/// <summary>MH-FNC-041 — minimum one active role at HTTP boundary.</summary>
[Collection(AuthHttp091Collection.Name)]
public sealed class MinimumActiveRoleHttpTests
{
    private readonly AuthWebApplicationFactory _factory;

    public MinimumActiveRoleHttpTests(AuthWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData(null, null)]
    [InlineData(new string[0], new string[0])]
    [Trait("Regression", "MH-FNC-041")]
    public async Task Toggle_empty_roles_returns_422_without_db_mutation(string[]? roleIds, string[]? roleNames)
    {
        var (userId, roleId, stampBefore) = await GetSecondUserRoleStateAsync();
        var admin = await LoginAdminAsync();

        var response = await admin.ToggleRolesAsync(new
        {
            UserId = userId,
            RoleIds = roleIds,
            RoleNames = roleNames
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(MinimumActiveRoleGuard.AtLeastOneRoleMessage, body, StringComparison.Ordinal);

        var (stampAfter, stillLinked) = await _factory.WithDbAsync(async db =>
        {
            var stamp = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).SecurityStamp;
            var linked = await db.UserRoleAssociations.AnyAsync(x =>
                x.UserId == userId && x.RoleId == roleId && x.IsActive);
            return (stamp, linked);
        });
        Assert.Equal(stampBefore, stampAfter);
        Assert.True(stillLinked);
    }

    [Fact]
    [Trait("Regression", "MH-FNC-041")]
    public async Task Patch_roles_empty_array_returns_422_without_stamp_change()
    {
        var (userId, roleId, stampBefore) = await GetSecondUserRoleStateAsync();
        var admin = await LoginAdminAsync();

        var response = await admin.PatchUserAsync(userId, new
        {
            fullName = "Second User",
            username = AuthTestSeed.SecondUserName,
            email = "second@it.local",
            employeeNumber = "EMP-2",
            rankOrTitle = "موظف",
            roles = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(MinimumActiveRoleGuard.AtLeastOneRoleMessage, body, StringComparison.Ordinal);
        Assert.Equal(stampBefore, await ReadStampAsync(userId));
    }

    [Fact]
    [Trait("Regression", "MH-FNC-041")]
    public async Task Patch_roles_null_leaves_roles_unchanged()
    {
        var (userId, roleId, stampBefore) = await GetSecondUserRoleStateAsync();
        var admin = await LoginAdminAsync();

        var response = await admin.PatchUserAsync(userId, new
        {
            fullName = "Second User Updated",
            username = AuthTestSeed.SecondUserName,
            email = "second@it.local",
            employeeNumber = "EMP-2",
            rankOrTitle = "موظف محدث"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await _factory.WithDbAsync(db =>
            db.UserRoleAssociations.AnyAsync(x => x.UserId == userId && x.RoleId == roleId && x.IsActive)));
        Assert.Equal(stampBefore, await ReadStampAsync(userId));
    }

    private async Task<AuthHttpSession> LoginAdminAsync()
    {
        var admin = _factory.CreateSession();
        Assert.Equal(HttpStatusCode.OK, (await admin.LoginAsync(AuthTestSeed.AdminUserName, AuthTestSeed.AdminPassword)).StatusCode);
        return admin;
    }

    private async Task<(string UserId, string RoleId, string Stamp)> GetSecondUserRoleStateAsync()
        => await _factory.WithDbAsync(async db =>
        {
            var userId = await db.Users.Where(u => u.UserName == AuthTestSeed.SecondUserName).Select(u => u.Id).SingleAsync();
            var roleId = await db.Roles.Where(r => r.Name == "IT-Member").Select(r => r.Id).SingleAsync();
            var stamp = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).SecurityStamp ?? "";
            return (userId, roleId, stamp);
        });

    private async Task<string> ReadStampAsync(string userId)
        => await _factory.WithDbAsync(async db =>
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).SecurityStamp ?? "");
}
