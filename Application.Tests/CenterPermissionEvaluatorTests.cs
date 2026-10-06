using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.AuthServices;
using Moq;

namespace Application.Tests;

public class CenterPermissionEvaluatorTests
{
    private static readonly Guid CenterA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CenterB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Central_permission_applies_without_an_active_center()
    {
        var names = CenterPermissionEvaluator.Resolve(
            roles: new[] { Role("r1", "Central", null, active: true, roleActive: true) },
            directs: new[] { Direct("Central.Direct", null, active: true, permissionActive: true) },
            links: new[] { Link("r1", "Central.Role", active: true, permissionActive: true) },
            activeCenterId: null);

        Assert.Contains("Central.Direct", names);
        Assert.Contains("Central.Role", names);
    }

    [Fact]
    public void Local_permission_applies_only_inside_its_center()
    {
        var roles = new[] { Role("r1", "Local", CenterA, true, true) };
        var links = new[] { Link("r1", "Local.Read", true, true) };
        var directs = new[] { Direct("Local.Direct", CenterA, true, true) };

        var inside = CenterPermissionEvaluator.Resolve(roles, directs, links, CenterA);
        var withoutCenter = CenterPermissionEvaluator.Resolve(roles, directs, links, null);

        Assert.Contains("Local.Read", inside);
        Assert.Contains("Local.Direct", inside);
        Assert.Empty(withoutCenter);
    }

    [Fact]
    public void Permission_of_center_a_does_not_apply_in_center_b()
    {
        var names = CenterPermissionEvaluator.Resolve(
            new[] { Role("r1", "Local", CenterA, true, true) },
            new[] { Direct("Local.Direct", CenterA, true, true) },
            new[] { Link("r1", "Local.Read", true, true) },
            CenterB);

        Assert.Empty(names);
    }

    [Fact]
    public void Disabled_role_grants_nothing()
    {
        var names = CenterPermissionEvaluator.Resolve(
            new[] { Role("r1", "Local", null, active: true, roleActive: false) },
            Array.Empty<CenterPermissionEvaluator.DirectAssignment>(),
            new[] { Link("r1", "Local.Read", true, true) },
            null);

        Assert.Empty(names);
    }

    [Fact]
    public void Disabled_permission_is_not_granted()
    {
        var names = CenterPermissionEvaluator.Resolve(
            new[] { Role("r1", "Central", null, true, true) },
            new[] { Direct("Off.Direct", null, active: true, permissionActive: false) },
            new[] { Link("r1", "Off.Role", active: true, permissionActive: false) },
            null);

        Assert.Empty(names);
    }

    [Fact]
    public void Revoked_assignment_is_not_used()
    {
        var names = CenterPermissionEvaluator.Resolve(
            new[] { Role("r1", "Central", null, active: false, roleActive: true) },
            new[] { Direct("Revoked.Direct", null, active: false, permissionActive: true) },
            new[] { Link("r1", "Revoked.Role", active: false, permissionActive: true) },
            null);

        Assert.Empty(names);
    }

    [Fact]
    public async Task User_cannot_select_a_center_they_are_not_assigned_to()
    {
        var access = new Mock<IAccessContextRepository>();
        access.Setup(a => a.LoadAsync("user-1", It.IsAny<CancellationToken>())).ReturnsAsync(new AccessSnapshot());
        access.Setup(a => a.IsActivelyAssignedAsync("user-1", CenterA, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.UserId).Returns("user-1");
        var tokens = new Mock<ITokenService>();

        var service = new ActiveCenterService(access.Object, current.Object, tokens.Object);

        await Assert.ThrowsAsync<ForbiddenPermissionException>(() => service.SelectAsync(CenterA));
        tokens.Verify(t => t.GenerateTokensAsync(It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
    }

    private static CenterPermissionEvaluator.RoleAssignment Role(string id, string name, Guid? center, bool active, bool roleActive)
        => new(id, name, center, active, roleActive);

    private static CenterPermissionEvaluator.DirectAssignment Direct(string name, Guid? center, bool active, bool permissionActive)
        => new(name, center, active, permissionActive);

    private static CenterPermissionEvaluator.RolePermissionLink Link(string roleId, string name, bool active, bool permissionActive)
        => new(roleId, name, active, permissionActive);
}
