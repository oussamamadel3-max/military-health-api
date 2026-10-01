namespace Application.Common.Localization
{
    public static class AuthDisplayNames
    {
        private static readonly Dictionary<string, string> RoleMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["SuperAdmin"] = "مدير النظام",
                ["SUPERADMIN"] = "مدير النظام",
            };

        private static readonly Dictionary<string, string> PermissionMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["UserManagement.Read"] = "قراءة بيانات المستخدمين",
                ["UserManagement.Create"] = "إنشاء مستخدمين جدد",
                ["UserManagement.Edit"] = "تعديل بيانات المستخدمين",
                ["UserManagement.Delete"] = "حذف المستخدمين",
                ["UserManagement.ChangeStatus"] = "تغيير حالة المستخدمين",
                ["UserManagement.AssignRoles"] = "إسناد أدوار للمستخدمين",
                ["UserManagement.AssignPermissions"] = "إسناد صلاحيات مباشرة للمستخدمين",
                ["UserManagement.ResetPassword"] = "إعادة تعيين كلمة مرور مستخدم",
                ["UserManagement.RemoveFieldPermissions"] = "إزالة صلاحيات الحقول عن مستخدم",

                ["RoleManagement.Read"] = "قراءة الأدوار",
                ["RoleManagement.Create"] = "إنشاء أدوار",
                ["RoleManagement.Edit"] = "تعديل بيانات الأدوار",
                ["RoleManagement.Delete"] = "حذف الأدوار",
                ["RoleManagement.ChangeStatus"] = "تغيير حالة الأدوار",
                ["RoleManagement.AssignPermissions"] = "إسناد صلاحيات للأدوار",

                ["PermissionManagement.Read"] = "قراءة تعريفات الصلاحيات",
                ["PermissionManagement.ChangeStatus"] = "تغيير حالة الصلاحيات",

                ["FieldPermissionManagement.Read"] = "قراءة صلاحيات الحقول",
                ["FieldPermissionManagement.Create"] = "إنشاء صلاحيات حقول",
                ["FieldPermissionManagement.Edit"] = "تعديل صلاحيات الحقول",
                ["FieldPermissionManagement.Delete"] = "حذف صلاحيات الحقول",
                ["FieldPermissionManagement.ChangeAccessType"] = "تغيير نوع وصول صلاحية الحقل",

                ["AuditLog.Read"] = "قراءة سجلات التدقيق",
                ["AuditLog.Export"] = "تصدير تقرير السجل",

                ["Dashboard.Read"] = "قراءة لوحة التحكم",
                ["Dashboard.Export"] = "تصدير تقرير لوحة التحكم",
            };

        private static readonly Dictionary<string, string> ModuleMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["UserManagement"] = "إدارة المستخدمين",
                ["RoleManagement"] = "إدارة الأدوار",
                ["PermissionManagement"] = "إدارة الصلاحيات",
                ["FieldPermissionManagement"] = "إدارة صلاحيات الحقول",
                ["AuditLog"] = "سجل التدقيق",
                ["Dashboard"] = "لوحة الإحصائيات",
            };

        private static readonly Dictionary<string, string> ActionMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Read"] = "قراءة",
                ["Create"] = "إنشاء",
                ["Edit"] = "تعديل",
                ["Delete"] = "حذف",
                ["Export"] = "تصدير",
                ["ChangeStatus"] = "تغيير الحالة",
                ["AssignRoles"] = "إسناد أدوار",
                ["AssignPermissions"] = "إسناد صلاحيات",
                ["ResetPassword"] = "إعادة تعيين كلمة المرور",
                ["RemoveFieldPermissions"] = "إزالة صلاحيات الحقول",
                ["ChangeAccessType"] = "تغيير نوع الوصول",
            };

        public static string RoleAr(string? roleNameOrNormalized)
        {
            if (string.IsNullOrWhiteSpace(roleNameOrNormalized)) return string.Empty;
            return RoleMap.TryGetValue(roleNameOrNormalized.Trim(), out var ar)
                ? ar
                : roleNameOrNormalized;
        }

        public static string PermissionAr(string? permissionName)
        {
            if (string.IsNullOrWhiteSpace(permissionName)) return string.Empty;
            return PermissionMap.TryGetValue(permissionName.Trim(), out var ar)
                ? ar
                : permissionName;
        }

        public static string ModuleAr(string? moduleKey)
        {
            if (string.IsNullOrWhiteSpace(moduleKey)) return string.Empty;
            return ModuleMap.TryGetValue(moduleKey.Trim(), out var ar)
                ? ar
                : moduleKey;
        }

        public static string ActionAr(string? actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey)) return string.Empty;
            return ActionMap.TryGetValue(actionKey.Trim(), out var ar)
                ? ar
                : actionKey;
        }
    }
}
