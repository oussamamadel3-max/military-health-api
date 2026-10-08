using Application.Common;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Services.Core;
using Moq;

namespace Application.Tests;

public class OrgScopeResolverTests
{
    private readonly Mock<IUserPermissionService> _userPermissions = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IUserOrgScopeRepository> _scopes = new();
    private readonly Mock<IOrgUnitRepository> _units = new();
    private readonly Guid _permissionId = Guid.NewGuid();

    private OrgScopeResolver Create(bool hasPermission, IReadOnlyList<OrgScopeLink>? scopes = null,
        IReadOnlyList<(Guid Id, Guid? ParentId)>? links = null)
    {
        _userPermissions.Setup(x => x.HasPermission("u", "P")).Returns(hasPermission);
        _permissions.Setup(x => x.FindActivePermissionIdByNameAsync("P", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_permissionId);
        _scopes.Setup(x => x.GetActiveScopesForPermissionAsync("u", _permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopes ?? Array.Empty<OrgScopeLink>());
        _units.Setup(x => x.GetNonArchivedParentLinksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(links ?? Array.Empty<(Guid, Guid?)>());
        return new(_userPermissions.Object, _permissions.Object, _scopes.Object, _units.Object);
    }

    [Fact]
    public async Task NoPermission_ReturnsEmptyWithoutReadingScopes()
    {
        Assert.Empty(await Create(false).GetAccessibleOrgUnitIdsAsync("u", "P"));
        _scopes.Verify(x => x.GetActiveScopesForPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Self_ReturnsOnlyScopedUnit()
    {
        var root = Guid.NewGuid(); var child = Guid.NewGuid();
        var result = await Create(true, new[] { new OrgScopeLink(root, ScopeModes.Self) },
            new[] { (child, (Guid?)root) }).GetAccessibleOrgUnitIdsAsync("u", "P");
        Assert.Equal(new[] { root }, result);
    }

    [Fact]
    public async Task Subtree_ReturnsDescendants()
    {
        var root = Guid.NewGuid(); var child = Guid.NewGuid();
        var result = await Create(true, new[] { new OrgScopeLink(root, ScopeModes.Subtree) },
            new[] { (child, (Guid?)root) }).GetAccessibleOrgUnitIdsAsync("u", "P");
        Assert.Equal(new HashSet<Guid> { root, child }, result.ToHashSet());
    }

    [Fact]
    public async Task MultipleScopes_ReturnUnionWithoutDuplicates()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var b1 = Guid.NewGuid();
        var b2 = Guid.NewGuid();
        var result = await Create(
                true,
                new[] { new OrgScopeLink(a, ScopeModes.Self), new OrgScopeLink(b, ScopeModes.Subtree) },
                new[] { (b1, (Guid?)b), (b2, (Guid?)b) })
            .GetAccessibleOrgUnitIdsAsync("u", "P");
        Assert.Equal(new HashSet<Guid> { a, b, b1, b2 }, result.ToHashSet());
    }

    [Fact]
    public async Task ArchivedCut_SubtreeDoesNotReachChildrenOfArchivedNode()
    {
        var a = Guid.NewGuid();
        var bArchived = Guid.NewGuid();
        var c = Guid.NewGuid();
        // Repository contract: archived nodes omitted from parent links (B missing → C orphaned under B)
        var result = await Create(
                true,
                new[] { new OrgScopeLink(a, ScopeModes.Subtree) },
                new[] { (c, (Guid?)bArchived) })
            .GetAccessibleOrgUnitIdsAsync("u", "P");
        Assert.Equal(new[] { a }, result);
    }

    [Fact]
    public async Task NoScopes_MeansEmptyUnitList_ForScopedResolver()
    {
        Assert.Empty(await Create(true, Array.Empty<OrgScopeLink>()).GetAccessibleOrgUnitIdsAsync("u", "P"));
    }

    [Fact]
    public async Task NoScopes_CanAccessAnyOrgUnit_AsGlobal()
    {
        var anyUnit = Guid.NewGuid();
        Assert.True(await Create(true, Array.Empty<OrgScopeLink>()).CanAccessOrgUnitAsync("u", "P", anyUnit));
    }

    [Fact]
    public async Task Scoped_CanAccessOnlyResolvedUnits()
    {
        var root = Guid.NewGuid();
        var other = Guid.NewGuid();
        var resolver = Create(true, new[] { new OrgScopeLink(root, ScopeModes.Self) });
        Assert.True(await resolver.CanAccessOrgUnitAsync("u", "P", root));
        Assert.False(await resolver.CanAccessOrgUnitAsync("u", "P", other));
    }
}
