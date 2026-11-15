namespace Application.Common.Security;

/// <summary>
/// الحساب الوحيد للصلاحيات حسب المركز. TokenService وUserPermissionService وPermissionGate يستدعونه.
/// </summary>
public static class CenterPermissionEvaluator
{
    public const string SourceRole = "Role";
    public const string SourceDirect = "Direct";

    public sealed record RoleAssignment(string RoleId, string? RoleName, Guid? CenterId, bool IsActive, bool RoleIsActive);
    public sealed record DirectAssignment(string? PermissionName, Guid? CenterId, bool IsActive, bool PermissionIsActive);
    public sealed record RolePermissionLink(string RoleId, string? PermissionName, bool IsActive, bool PermissionIsActive);

    public sealed record PermissionSource(string SourceType, string? RoleId, string? RoleName);

    public sealed record EffectivePermission(
        string Name,
        bool FromRole,
        bool FromDirect,
        IReadOnlyList<PermissionSource> Sources);

    public static bool AppliesToContext(Guid? assignmentCenterId, Guid? activeCenterId)
    {
        if (assignmentCenterId is null)
            return true;

        return activeCenterId.HasValue && assignmentCenterId.Value == activeCenterId.Value;
    }

    public static IReadOnlyList<string> Resolve(
        IEnumerable<RoleAssignment> roles,
        IEnumerable<DirectAssignment> directs,
        IEnumerable<RolePermissionLink> links,
        Guid? activeCenterId)
        => ResolveDetailed(roles, directs, links, activeCenterId)
            .Select(p => p.Name)
            .ToList();

    /// <summary>
    /// نفس قواعد Resolve مع مصادر كل صلاحية (دور / مباشرة) لضمان تطابق العرض مع PermissionGate.
    /// </summary>
    public static IReadOnlyList<EffectivePermission> ResolveDetailed(
        IEnumerable<RoleAssignment> roles,
        IEnumerable<DirectAssignment> directs,
        IEnumerable<RolePermissionLink> links,
        Guid? activeCenterId)
    {
        var roleList = roles as IList<RoleAssignment> ?? roles.ToList();
        var applicableRoles = roleList
            .Where(r => r.IsActive && r.RoleIsActive && AppliesToContext(r.CenterId, activeCenterId))
            .ToList();
        var applicableRoleIds = applicableRoles
            .Select(r => r.RoleId)
            .ToHashSet(StringComparer.Ordinal);
        var roleNameById = applicableRoles
            .GroupBy(r => r.RoleId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.RoleName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.Ordinal);

        var bucket = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            if (!link.IsActive || !link.PermissionIsActive || string.IsNullOrWhiteSpace(link.PermissionName))
                continue;
            if (!applicableRoleIds.Contains(link.RoleId))
                continue;

            roleNameById.TryGetValue(link.RoleId, out var roleName);
            var entry = GetOrAdd(bucket, link.PermissionName);
            entry.FromRole = true;
            entry.Sources.Add(new PermissionSource(SourceRole, link.RoleId, roleName));
        }

        foreach (var direct in directs)
        {
            if (!direct.IsActive || !direct.PermissionIsActive || string.IsNullOrWhiteSpace(direct.PermissionName))
                continue;
            if (!AppliesToContext(direct.CenterId, activeCenterId))
                continue;

            var entry = GetOrAdd(bucket, direct.PermissionName);
            entry.FromDirect = true;
            entry.Sources.Add(new PermissionSource(SourceDirect, null, null));
        }

        return bucket
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new EffectivePermission(
                kv.Key,
                kv.Value.FromRole,
                kv.Value.FromDirect,
                DeduplicateSources(kv.Value.Sources)))
            .ToList();
    }

    public static IReadOnlyList<string> ApplicableRoleNames(
        IEnumerable<RoleAssignment> roles,
        Guid? activeCenterId)
    {
        return roles
            .Where(r => r.IsActive && r.RoleIsActive && !string.IsNullOrWhiteSpace(r.RoleName) && AppliesToContext(r.CenterId, activeCenterId))
            .Select(r => r.RoleName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>معرّفات الأدوار النشطة المنطبقة على سياق المركز الحالي.</summary>
    public static IReadOnlyList<string> ApplicableRoleIds(
        IEnumerable<RoleAssignment> roles,
        Guid? activeCenterId)
    {
        return roles
            .Where(r => r.IsActive && r.RoleIsActive && !string.IsNullOrWhiteSpace(r.RoleId) && AppliesToContext(r.CenterId, activeCenterId))
            .Select(r => r.RoleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// أهلية سياق مركزي/جلسة (أي دور أو صلاحية مباشرة بـ CenterId فارغ).
    /// لا تُستخدم وحدها لمنح نطاق إدارة مستخدمين غير مقيد (BR-057).
    /// </summary>
    public static bool HasCentralAuthority(
        IEnumerable<RoleAssignment> roles,
        IEnumerable<DirectAssignment> directs)
    {
        var centralRole = roles.Any(r => r.IsActive && r.RoleIsActive && r.CenterId is null);
        var centralDirect = directs.Any(d => d.IsActive && d.PermissionIsActive && d.CenterId is null && !string.IsNullOrWhiteSpace(d.PermissionName));
        return centralRole || centralDirect;
    }

    /// <summary>
    /// هل يملك المنفّذ الصلاحية المسماة ضمن نطاق مركزي فعّال (دور/مباشر بـ CenterId فارغ)؟
    /// يطابق حساب <c>Resolve(..., activeCenterId: null)</c>.
    /// </summary>
    public static bool HasCentralOperationPermission(
        IEnumerable<RoleAssignment> roles,
        IEnumerable<DirectAssignment> directs,
        IEnumerable<RolePermissionLink> links,
        string operationPermission)
    {
        if (string.IsNullOrWhiteSpace(operationPermission))
            return false;

        return Resolve(roles, directs, links, activeCenterId: null)
            .Contains(operationPermission, StringComparer.OrdinalIgnoreCase);
    }

    private static Entry GetOrAdd(Dictionary<string, Entry> bucket, string name)
    {
        if (!bucket.TryGetValue(name, out var entry))
        {
            entry = new Entry();
            bucket[name] = entry;
        }

        return entry;
    }

    private static IReadOnlyList<PermissionSource> DeduplicateSources(List<PermissionSource> sources)
    {
        return sources
            .GroupBy(
                s => $"{s.SourceType}|{s.RoleId ?? string.Empty}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private sealed class Entry
    {
        public bool FromRole { get; set; }
        public bool FromDirect { get; set; }
        public List<PermissionSource> Sources { get; } = new();
    }
}
