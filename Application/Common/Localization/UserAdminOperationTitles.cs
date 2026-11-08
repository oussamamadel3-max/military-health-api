using Application.Common.Security;
using Domain.Entities.Audit;
using Domain.Entities.Auth;

namespace Application.Common.Localization;

/// <summary>
/// ترجمة أحداث التدقيق المرتبطة بحساب مستخدم إلى عناوين عربية للواجهة.
/// </summary>
public static class UserAdminOperationTitles
{
    private static readonly HashSet<string> ExcludedEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Login",
        "Logout",
        "CenterChange",
        "READ",
        "VIEW"
    };

    private static readonly HashSet<string> AccountEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(User),
        nameof(UserRoleAssociation),
        nameof(UserPermission),
        nameof(UserFieldPermission),
        nameof(UserCenter),
        nameof(UserClinicAssignment)
    };

    public static bool IsAccountAdminCandidate(AuditEvent e)
    {
        if (e is null) return false;
        if (!string.IsNullOrWhiteSpace(e.EventType) && ExcludedEventTypes.Contains(e.EventType))
            return false;

        if (string.Equals(e.EventType, UserAccountAdminEventTypes.PasswordReset, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.EventType, UserAccountAdminEventTypes.SessionsInvalidated, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.EventType, UserAccountAdminEventTypes.AccountLocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.EventType, UserAccountAdminEventTypes.AccountUnlocked, StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(e.EntityType) && AccountEntityTypes.Contains(e.EntityType);
    }

    public static (string? Title, string? Detail) TryMap(AuditEvent e)
    {
        if (!IsAccountAdminCandidate(e))
            return (null, null);

        if (string.Equals(e.EventType, UserAccountAdminEventTypes.PasswordReset, StringComparison.OrdinalIgnoreCase))
            return ("إعادة تعيين كلمة المرور", "بواسطة مسؤول — دون تسجيل قيمة كلمة المرور");

        if (string.Equals(e.EventType, UserAccountAdminEventTypes.SessionsInvalidated, StringComparison.OrdinalIgnoreCase))
            return ("إبطال الجلسات", e.ReasonText);

        if (string.Equals(e.EventType, UserAccountAdminEventTypes.AccountLocked, StringComparison.OrdinalIgnoreCase))
        {
            var duration = AuditEventValueReader.ReadString(e.NewValues, "durationSeconds");
            return ("قفل الحساب", e.ReasonText ?? (string.IsNullOrWhiteSpace(duration) ? null : "مدة القفل (ثوانٍ): " + duration));
        }

        if (string.Equals(e.EventType, UserAccountAdminEventTypes.AccountUnlocked, StringComparison.OrdinalIgnoreCase))
            return ("فك قفل الحساب", e.ReasonText);

        var entity = e.EntityType ?? string.Empty;
        var state = e.EventType ?? string.Empty;

        if (string.Equals(entity, nameof(User), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(state, "Added", StringComparison.OrdinalIgnoreCase))
                return ("إنشاء المستخدم", null);

            if (string.Equals(state, "Modified", StringComparison.OrdinalIgnoreCase))
            {
                if (AuditEventValueReader.ReasonMentions(e, "IsActive"))
                {
                    var active = AuditEventValueReader.ReadBool(e.NewValues, "IsActive");
                    if (active == true) return ("تفعيل الحساب", null);
                    if (active == false) return ("تعطيل الحساب", null);
                    return ("تغيير حالة الحساب", e.ReasonText);
                }

                if (AuditEventValueReader.ReasonOnlyMentions(e, "MustChangePassword"))
                    return (null, null);

                return ("تعديل بيانات المستخدم", AuditEventValueReader.SummarizeChangedFields(e.ReasonText));
            }

            if (string.Equals(state, "Deleted", StringComparison.OrdinalIgnoreCase))
                return ("حذف/إلغاء حساب المستخدم", null);
        }

        if (string.Equals(entity, nameof(UserRoleAssociation), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(state, "Added", StringComparison.OrdinalIgnoreCase))
                return ("إضافة دور", FormatRoleId(e.NewValues));
            if (AuditEventValueReader.IsDeactivation(e))
                return ("إزالة دور", FormatRoleId(e.OldValues) ?? FormatRoleId(e.NewValues));
            if (string.Equals(state, "Modified", StringComparison.OrdinalIgnoreCase))
                return ("تعديل إسناد دور", FormatRoleId(e.NewValues));
            if (string.Equals(state, "Deleted", StringComparison.OrdinalIgnoreCase))
                return ("إزالة دور", FormatRoleId(e.OldValues));
        }

        if (string.Equals(entity, nameof(UserPermission), StringComparison.OrdinalIgnoreCase)
            || string.Equals(entity, nameof(UserFieldPermission), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(state, "Added", StringComparison.OrdinalIgnoreCase))
                return ("تعديل الصلاحيات", "منح صلاحية");
            if (AuditEventValueReader.IsDeactivation(e) || string.Equals(state, "Deleted", StringComparison.OrdinalIgnoreCase))
                return ("تعديل الصلاحيات", "إلغاء صلاحية");
            if (string.Equals(state, "Modified", StringComparison.OrdinalIgnoreCase))
                return ("تعديل الصلاحيات", e.ReasonText);
        }

        if (string.Equals(entity, nameof(UserCenter), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(state, "Added", StringComparison.OrdinalIgnoreCase))
                return ("ربط المستخدم بمركز طبي", FormatCenter(e.NewValues, e.EntityId));
            if (AuditEventValueReader.IsDeactivation(e) || string.Equals(state, "Deleted", StringComparison.OrdinalIgnoreCase))
                return ("إزالة مركز من نطاق المستخدم", FormatCenter(e.OldValues, e.EntityId) ?? FormatCenter(e.NewValues, e.EntityId));
            if (string.Equals(state, "Modified", StringComparison.OrdinalIgnoreCase))
                return ("تغيير المراكز المرتبطة", FormatCenter(e.NewValues, e.EntityId));
        }

        if (string.Equals(entity, nameof(UserClinicAssignment), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(state, "Added", StringComparison.OrdinalIgnoreCase))
                return ("إسناد عيادة للمستخدم", null);
            if (AuditEventValueReader.IsDeactivation(e) || string.Equals(state, "Deleted", StringComparison.OrdinalIgnoreCase))
                return ("إلغاء إسناد عيادة", null);
            if (string.Equals(state, "Modified", StringComparison.OrdinalIgnoreCase))
                return ("تعديل إسناد عيادة", e.ReasonText);
        }

        return (null, null);
    }

    private static string? FormatRoleId(string? json)
    {
        var roleId = AuditEventValueReader.ReadString(json, "RoleId");
        return string.IsNullOrWhiteSpace(roleId) ? null : "معرّف الدور: " + roleId;
    }

    private static string? FormatCenter(string? json, string? entityId)
    {
        var centerId = AuditEventValueReader.ReadString(json, "CenterId");
        if (string.IsNullOrWhiteSpace(centerId) && !string.IsNullOrWhiteSpace(entityId))
        {
            var colon = entityId.IndexOf(':');
            if (colon >= 0 && colon < entityId.Length - 1)
                centerId = entityId[(colon + 1)..];
        }

        return string.IsNullOrWhiteSpace(centerId) ? null : "معرّف المركز: " + centerId;
    }
}
