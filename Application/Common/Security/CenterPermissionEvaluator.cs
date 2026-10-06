namespace Application.Common.Security;

/// <summary>
/// الحساب الوحيد للصلاحيات حسب المركز. TokenService وUserPermissionService يستدعيانه.
/// </summary>
public static class CenterPermissionEvaluator
{
    public sealed record RoleAssignment(string RoleId, string? RoleName, Guid? CenterId, bool IsActive, bool RoleIsActive);
    public sealed record DirectAssignment(string? PermissionName, Guid? CenterId, bool IsActive, bool PermissionIsActive);
    public sealed record RolePermissionLink(string RoleId, string? PermissionName, bool IsActive, bool PermissionIsActive);

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
    {
        var applicableRoleIds = roles
            .Where(r => r.IsActive && r.RoleIsActive && AppliesToContext(r.CenterId, activeCenterId))
            .Select(r => r.RoleId)
            .ToHashSet(StringComparer.Ordinal);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            if (!link.IsActive || !link.PermissionIsActive || string.IsNullOrWhiteSpace(link.PermissionName))
                continue;
            if (applicableRoleIds.Contains(link.RoleId))
                names.Add(link.PermissionName);
        }

        foreach (var direct in directs)
        {
            if (!direct.IsActive || !direct.PermissionIsActive || string.IsNullOrWhiteSpace(direct.PermissionName))
                continue;
            if (AppliesToContext(direct.CenterId, activeCenterId))
                names.Add(direct.PermissionName);
        }

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
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

    public static bool HasCentralAuthority(
        IEnumerable<RoleAssignment> roles,
        IEnumerable<DirectAssignment> directs)
    {
        var centralRole = roles.Any(r => r.IsActive && r.RoleIsActive && r.CenterId is null);
        var centralDirect = directs.Any(d => d.IsActive && d.PermissionIsActive && d.CenterId is null && !string.IsNullOrWhiteSpace(d.PermissionName));
        return centralRole || centralDirect;
    }
}
