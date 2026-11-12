using Application.Common.Permissions;

namespace Application.Common.Security;

/// <summary>
/// يصنّف رفض الوصول الواجب تدقيقه كحدث IAM حساس (FR-076 / NFR-017)،
/// دون توسيع التسجيل لكل رفض صلاحية تشغيلية عامة عبر PermissionGate.
/// </summary>
public static class IamSensitiveAccessDenial
{
    /// <summary>
    /// كتالوج الصلاحيات الحساسة المعروفة + وحدات IAM/التدقيق.
    /// البادئة وحدها ليست المعيار الوحيد؛ الكتالوج يغطي الثوابت المعتمدة.
    /// </summary>
    private static readonly HashSet<string> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        SystemPermissions.UserManagement.Read,
        SystemPermissions.UserManagement.ViewEffectiveAccess,
        SystemPermissions.UserManagement.Create,
        SystemPermissions.UserManagement.Edit,
        SystemPermissions.UserManagement.Delete,
        SystemPermissions.UserManagement.ChangeStatus,
        SystemPermissions.UserManagement.AssignRoles,
        SystemPermissions.UserManagement.AssignPermissions,
        SystemPermissions.UserManagement.ResetPassword,
        SystemPermissions.UserManagement.RemoveFieldPermissions,

        SystemPermissions.RoleManagement.Read,
        SystemPermissions.RoleManagement.Create,
        SystemPermissions.RoleManagement.Edit,
        SystemPermissions.RoleManagement.Delete,
        SystemPermissions.RoleManagement.ChangeStatus,
        SystemPermissions.RoleManagement.AssignPermissions,

        SystemPermissions.PermissionManagement.Read,
        SystemPermissions.PermissionManagement.ChangeStatus,

        SystemPermissions.FieldPermissionManagement.Read,
        SystemPermissions.FieldPermissionManagement.Create,
        SystemPermissions.FieldPermissionManagement.Edit,
        SystemPermissions.FieldPermissionManagement.Delete,
        SystemPermissions.FieldPermissionManagement.ChangeAccessType,

        SystemPermissions.AuditLog.Read,
        SystemPermissions.AuditLog.Export,
        MilitaryHealthPermissions.AuditLog.Modify,
    };

    private static readonly string[] ModulePrefixes =
    [
        "UserManagement.",
        "RoleManagement.",
        "PermissionManagement.",
        "FieldPermissionManagement.",
        "AuditLog.",
    ];

    private static readonly string[] SensitiveOperations =
    [
        "UserManagementScopeGuard",
        "IamAdminGuard",
        "AuthorizePermissionAttribute",
    ];

    /// <summary>
    /// يقرر إن كانت الصلاحية (أو قائمة مفصولة بفواصل) تستوجب AuditEvent عند الرفض.
    /// </summary>
    public static bool RequiresAuditForPermission(string? permissionOrCsv)
    {
        if (string.IsNullOrWhiteSpace(permissionOrCsv))
            return false;

        foreach (var raw in permissionOrCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Catalog.Contains(raw))
                return true;

            // تغطية صلاحيات مستقبلية ضمن وحدات IAM/التدقيق دون توسيع للوحدات التشغيلية.
            foreach (var prefix in ModulePrefixes)
            {
                if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// عمليات الحراسة التي تكون بطبيعتها IAM حتى بدون صلاحية صريحة في الاستثناء.
    /// </summary>
    public static bool RequiresAuditForOperation(string? operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
            return false;

        foreach (var marker in SensitiveOperations)
        {
            if (operation.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
