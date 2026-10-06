using Application.Interfaces.Services;
using Application.Services.Core;
using Moq;

namespace Application.Tests;

public class OrgAccessServiceTests
{
    [Fact]
    public async Task WithoutPermission_ReturnsNoAccess()
    {
        var resolver = new Mock<IOrgScopeResolver>();
        resolver.Setup(x => x.UserHasBasePermission("u", "P")).Returns(false);

        var result = await new OrgAccessService(resolver.Object).GetAccessAsync("u", "P");

        Assert.False(result.HasPermission);
        Assert.False(result.HasGlobalAccess);
        Assert.Empty(result.OrgUnitIds);
        resolver.Verify(x => x.GetAccessibleOrgUnitIdsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PermissionWithoutScopes_ReturnsGlobalAccess()
    {
        var resolver = new Mock<IOrgScopeResolver>();
        resolver.Setup(x => x.UserHasBasePermission("u", "P")).Returns(true);
        resolver.Setup(x => x.GetAccessibleOrgUnitIdsAsync("u", "P", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var result = await new OrgAccessService(resolver.Object).GetAccessAsync("u", "P");

        Assert.True(result.HasPermission);
        Assert.True(result.HasGlobalAccess);
        Assert.Empty(result.OrgUnitIds);
    }

    [Fact]
    public async Task PermissionWithScopes_ReturnsScopedUnits()
    {
        var unitIds = new List<Guid> { Guid.NewGuid() };
        var resolver = new Mock<IOrgScopeResolver>();
        resolver.Setup(x => x.UserHasBasePermission("u", "P")).Returns(true);
        resolver.Setup(x => x.GetAccessibleOrgUnitIdsAsync("u", "P", It.IsAny<CancellationToken>())).ReturnsAsync(unitIds);

        var result = await new OrgAccessService(resolver.Object).GetAccessAsync("u", "P");

        Assert.True(result.HasPermission);
        Assert.False(result.HasGlobalAccess);
        Assert.Equal(unitIds, result.OrgUnitIds);
    }
}
