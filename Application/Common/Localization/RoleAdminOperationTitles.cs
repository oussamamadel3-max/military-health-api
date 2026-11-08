using Domain.Entities.Audit;
using Domain.Entities.Auth;

namespace Application.Common.Localization;

/// <summary>
/// ترجمة أحداث التدقيق المرتبطة بدور مستهدف إلى عناوين عربية للواجهة.
/// </summary>
public static class RoleAdminOperationTitles
{
    private static readonly HashSet<string> RoleEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Role),
        nameof(RolePermission),
        nameof(RoleFieldPermission)
    };

    public static bool IsRoleAdminCandidate(AuditEvent e)
        => e is not null
           && !string.IsNullOrWhiteSpace(e.EntityType)
           && RoleEntityTypes.Contains(e.EntityType);

    public static (string? Title, string? Detail) TryMap(AuditEvent e)
    {
        if (!IsRoleAdminCandidate(e))
            return (null, null);

        var entity = e.EntityType ?? string.Empty;
        var state = e.EventType ?? string.Empty;

        if (string.Equals(entity, nameof(Role), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(state, "Added", StringComparison.OrdinalIgnoreCase))
                return ("إنشاء الدور", null);

            if (string.Equals(state, "Modified", StringComparison.OrdinalIgnoreCase))
            {
                if (AuditEventValueReader.ReasonMentions(e, "IsActive"))
                {
                    var active = AuditEventValueReader.ReadBool(e.NewValues, "IsActive");
                    if (active == true) return ("تفعيل الدور", null);
                    if (active == false) return ("تعطيل الدور", null);
                    return ("تغيير حالة الدور", e.ReasonText);
                }

                return ("تعديل بيانات الدور", AuditEventValueReader.SummarizeChangedFields(e.ReasonText));
            }

            if (string.Equals(state, "Deleted", StringComparison.OrdinalIgnoreCase))
                return ("حذف/إلغاء الدور", null);
        }

        if (string.Equals(entity, nameof(RolePermission), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(state, "Added", StringComparison.OrdinalIgnoreCase))
                return ("إضافة صلاحية", PermissionDetail(e.NewValues));

            if (AuditEventValueReader.IsDeactivation(e)
                || string.Equals(state, "Deleted", StringComparison.OrdinalIgnoreCase))
                return ("إزالة صلاحية", PermissionDetail(e.OldValues) ?? PermissionDetail(e.NewValues));

            if (string.Equals(state, "Modified", StringComparison.OrdinalIgnoreCase))
                return ("تعديل صلاحية الدور", PermissionDetail(e.NewValues) ?? e.ReasonText);
        }

        if (string.Equals(entity, nameof(RoleFieldPermission), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(state, "Added", StringComparison.OrdinalIgnoreCase))
                return ("تعديل صلاحيات الحقول", "منح صلاحية حقل");

            if (string.Equals(state, "Deleted", StringComparison.OrdinalIgnoreCase)
                || AuditEventValueReader.IsDeactivation(e))
                return ("تعديل صلاحيات الحقول", "إلغاء صلاحية حقل");

            if (string.Equals(state, "Modified", StringComparison.OrdinalIgnoreCase))
                return ("تعديل صلاحيات الحقول", e.ReasonText);
        }

        return ("عملية على الدور", e.ReasonText);
    }

    private static string? PermissionDetail(string? json)
    {
        var permissionId = AuditEventValueReader.ReadString(json, "PermissionId");
        return string.IsNullOrWhiteSpace(permissionId) ? null : "معرّف الصلاحية: " + permissionId;
    }
}
