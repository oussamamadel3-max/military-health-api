using Application.Dtos.CoreDtos;
using Application.Services.Core;

namespace Application.Tests;

public class OrgUnitTreeHelperTests
{
    [Fact]
    public void BuildTree_BuildsAndSortsHierarchy()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var tree = OrgUnitTreeHelper.BuildTree(new[]
        {
            new OrgUnitTreeNodeDto { OrgUnitId = child, ParentOrgUnitId = root, NameAr = "ب" },
            new OrgUnitTreeNodeDto { OrgUnitId = root, NameAr = "أ" }
        });
        Assert.Single(tree);
        Assert.Equal(child, Assert.Single(tree[0].Children).OrgUnitId);
    }

    [Fact]
    public void WouldCreateCycle_DetectsDescendantParent()
    {
        var root = Guid.NewGuid();
        var child = Guid.NewGuid();
        var map = OrgUnitTreeHelper.BuildChildrenMap(new[] { (root, (Guid?)null), (child, (Guid?)root) });
        Assert.True(OrgUnitTreeHelper.WouldCreateCycle(root, child, map));
    }

    [Fact]
    public void GetDescendantIds_ReturnsAllLevels()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var map = OrgUnitTreeHelper.BuildChildrenMap(new[] { (b, (Guid?)a), (c, (Guid?)b) });
        Assert.Equal(new HashSet<Guid> { b, c }, OrgUnitTreeHelper.GetDescendantIds(a, map));
    }

    [Fact]
    public void ArchivedCut_ExcludingArchivedLinkCutsItsSubtree()
    {
        var root = Guid.NewGuid(); var archived = Guid.NewGuid(); var child = Guid.NewGuid();
        var map = OrgUnitTreeHelper.BuildChildrenMap(new[] { (root, (Guid?)null), (child, (Guid?)archived) });
        Assert.Empty(OrgUnitTreeHelper.GetDescendantIds(root, map));
    }
}
