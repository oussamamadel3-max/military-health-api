using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Services.Core;
using Moq;

namespace Application.Tests;

public class OrgAccessServiceTests
{
    [Fact]
    public async Task SuperAdmin_HasGlobalAccess()
    {
        var resolver = new Mock<IOrgScopeResolver>();
        resolver.Setup(x => x.UserHasBasePermission("u", "P")).Returns(true);
        var actor = new Mock<IActorScopeProvider>();
        actor.Setup(x => x.IsSuperAdminAsync("u", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await new OrgAccessService(resolver.Object, actor.Object).GetAccessAsync("u", "P");

        Assert.True(result.HasPermission);
        Assert.True(result.HasGlobalAccess);
        Assert.Empty(result.OrgUnitIds);
        resolver.Verify(x => x.GetAccessibleOrgUnitIdsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
