using Application.Dtos.CoreDtos;

namespace Application.Services.Core;

public static class OrgUnitTreeHelper
{
    public static List<OrgUnitTreeNodeDto> BuildTree(IEnumerable<OrgUnitTreeNodeDto> flat, Guid? rootParentId = null)
    {
        var lookup = flat.ToLookup(x => x.ParentOrgUnitId);
        return BuildChildren(lookup, rootParentId, new HashSet<Guid>());
    }

    private static List<OrgUnitTreeNodeDto> BuildChildren(
        ILookup<Guid?, OrgUnitTreeNodeDto> lookup, Guid? parentId, HashSet<Guid> path) =>
        lookup[parentId].OrderBy(n => n.NameAr).Where(n => !path.Contains(n.OrgUnitId)).Select(n =>
        {
            var nextPath = new HashSet<Guid>(path) { n.OrgUnitId };
            n.Children = BuildChildren(lookup, n.OrgUnitId, nextPath);
            return n;
        }).ToList();

    public static Dictionary<Guid, List<Guid>> BuildChildrenMap(IEnumerable<(Guid Id, Guid? ParentId)> units)
    {
        var map = new Dictionary<Guid, List<Guid>>();
        foreach (var (id, parentId) in units)
        {
            if (parentId is null) continue;
            if (!map.TryGetValue(parentId.Value, out var children))
                map[parentId.Value] = children = new List<Guid>();
            children.Add(id);
        }
        return map;
    }

    public static HashSet<Guid> GetDescendantIds(Guid parentId, IReadOnlyDictionary<Guid, List<Guid>> childrenByParent)
    {
        var result = new HashSet<Guid>();
        if (!childrenByParent.TryGetValue(parentId, out var children)) return result;
        var stack = new Stack<Guid>(children);
        while (stack.TryPop(out var id))
        {
            if (!result.Add(id)) continue;
            if (childrenByParent.TryGetValue(id, out var nested))
                foreach (var child in nested) stack.Push(child);
        }
        return result;
    }

    public static bool WouldCreateCycle(Guid unitId, Guid? newParentId,
        IReadOnlyDictionary<Guid, List<Guid>> childrenByParent) =>
        newParentId is not null &&
        (newParentId == unitId || GetDescendantIds(unitId, childrenByParent).Contains(newParentId.Value));
}
