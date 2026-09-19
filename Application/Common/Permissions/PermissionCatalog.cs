namespace Application.Common.Permissions;

public static class PermissionCatalog
{
    public static readonly HashSet<string> ObsoletePermissionExactNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SuperAdmin",
            "BranchManager",
        };

    public static readonly HashSet<string> ObsoleteModules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Document",
            "ReportsDocuments",
        };

    public static bool IsObsoletePermissionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var trimmed = name.Trim();
        if (ObsoletePermissionExactNames.Contains(trimmed))
            return true;

        var moduleKey = GetModuleKey(trimmed);
        return ObsoleteModules.Contains(moduleKey);
    }

    public static string GetModuleKey(string permissionName)
    {
        if (string.IsNullOrWhiteSpace(permissionName)) return string.Empty;

        var trimmed = permissionName.Trim();
        var dot = trimmed.IndexOf('.');
        return dot < 0 ? trimmed : trimmed[..dot];
    }

    public static string GetActionKey(string permissionName)
    {
        if (string.IsNullOrWhiteSpace(permissionName)) return string.Empty;

        var trimmed = permissionName.Trim();
        var dot = trimmed.IndexOf('.');
        return dot < 0 || dot >= trimmed.Length - 1
            ? string.Empty
            : trimmed[(dot + 1)..];
    }
}
