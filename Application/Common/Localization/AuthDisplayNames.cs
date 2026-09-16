namespace Application.Common.Localization
{
    public static class AuthDisplayNames
    {
        // ✅ ترجمة أسماء الأدوار حسب Name/NormalizedName
        private static readonly Dictionary<string, string> RoleMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["SuperAdmin"] = "مدير النظام",
                ["SUPERADMIN"] = "مدير النظام",

                ["BranchManager"] = "مدير الفرع",
                ["BRANCHMANAGER"] = "مدير الفرع",

                ["DepartmentUser"] = "مستخدم",
                ["DEPARTMENTUSER"] = "مستخدم",
            };

        // ✅ ترجمة أسماء الصلاحيات حسب Permission.Name (الإنكليزي)
        private static readonly Dictionary<string, string> PermissionMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // User Management
                ["UserManagement.Read"] = "قراءة بيانات المستخدمين",
                ["UserManagement.Create"] = "إنشاء مستخدمين جدد",
                ["UserManagement.Edit"] = "تعديل بيانات المستخدمين",
                ["UserManagement.Delete"] = "حذف المستخدمين",

                // Audit
                ["AuditLog.Read"] = "قراءة سجلات التدقيق",

                // Dashboard & Reports
                ["Dashboard.Read"] = "قراءة لوحة التحكم",
                ["ReportsAuditLogs.Export"] = "تصدير تقرير السجل",
                ["ReportsDashboard.Export"] = "تصدير تقرير لوحة التحكم",
            };

        private static readonly Dictionary<string, string> ModuleMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["UserManagement"] = "إدارة المستخدمين",
                ["AuditLog"] = "سجل التدقيق",
                ["Dashboard"] = "لوحة الإحصائيات",
                ["ReportsAuditLogs"] = "تقارير سجل التدقيق",
                ["ReportsDashboard"] = "تقارير لوحة التحكم",
            };

        private static readonly Dictionary<string, string> ActionMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Read"] = "قراءة",
                ["Create"] = "إنشاء",
                ["Edit"] = "تعديل",
                ["Delete"] = "حذف",
                ["Export"] = "تصدير",
            };

        public static string RoleAr(string? roleNameOrNormalized)
        {
            if (string.IsNullOrWhiteSpace(roleNameOrNormalized)) return string.Empty;
            return RoleMap.TryGetValue(roleNameOrNormalized.Trim(), out var ar)
                ? ar
                : roleNameOrNormalized; // fallback: رجّع الأصل
        }

        public static string PermissionAr(string? permissionName)
        {
            if (string.IsNullOrWhiteSpace(permissionName)) return string.Empty;
            return PermissionMap.TryGetValue(permissionName.Trim(), out var ar)
                ? ar
                : permissionName; // fallback
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
