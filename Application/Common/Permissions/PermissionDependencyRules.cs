using Application.Common.Localization;
using Application.Exceptions;

namespace Application.Common.Permissions;

public static class PermissionDependencyRules
{
    public const string RequiredAction = "Read";

    public static readonly HashSet<string> ActionsRequiringRead =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Create",
            "Edit",
            "Delete",
        };

    /// <summary>
    /// يمنع تعيين إنشاء/تعديل/حذف لوحدة بدون صلاحية القراءة لنفس الوحدة.
    /// </summary>
    public static void EnsureReadBeforeWriteActions(
        IEnumerable<string?> permissionNames,
        Func<string, Exception>? createException = null)
    {
        createException ??= static message => new InvalidRoleDataException(message);

        var moduleActions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawName in permissionNames)
        {
            if (string.IsNullOrWhiteSpace(rawName)) continue;

            var name = rawName.Trim();
            var moduleKey = GetModuleKey(name);
            var actionKey = GetActionKey(name);

            if (string.IsNullOrWhiteSpace(moduleKey) || string.IsNullOrWhiteSpace(actionKey))
                continue;

            if (!moduleActions.TryGetValue(moduleKey, out var actions))
            {
                actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                moduleActions[moduleKey] = actions;
            }

            actions.Add(actionKey);
        }

        foreach (var (moduleKey, actions) in moduleActions)
        {
            var writeActions = actions
                .Where(a => ActionsRequiringRead.Contains(a))
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (writeActions.Count == 0) continue;
            if (actions.Contains(RequiredAction)) continue;

            var moduleLabel = AuthDisplayNames.ModuleAr(moduleKey);
            var actionLabels = string.Join(" و", writeActions.Select(AuthDisplayNames.ActionAr));

            throw createException(
                $"لا يمكن تعيين صلاحيات ({actionLabels}) لوحدة «{moduleLabel}» بدون صلاحية القراءة.");
        }
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
