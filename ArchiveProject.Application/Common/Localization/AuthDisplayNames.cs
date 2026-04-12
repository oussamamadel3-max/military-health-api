namespace ArchiveProject.Application.Common.Localization
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

                ["DepartmentUser"] = "موظف قسم",
                ["DEPARTMENTUSER"] = "موظف قسم",
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
                ["ReportsDocuments.Export"] = "تصدير تقرير الوثائق",
                ["ReportsAuditLogs.Export"] = "تصدير تقرير السجل",
                ["ReportsDashboard.Export"] = "تصدير تقرير لوحة التحكم",

                // Department CRUD
                ["Department.Read"] = "قراءة بيانات الأقسام",
                ["Department.Create"] = "إنشاء أقسام جديدة",
                ["Department.Edit"] = "تعديل بيانات الأقسام",
                ["Department.Delete"] = "حذف الأقسام",

                // DocumentType CRUD
                ["DocumentType.Read"] = "قراءة أنواع الوثائق",
                ["DocumentType.Create"] = "إنشاء أنواع وثائق جديدة",
                ["DocumentType.Edit"] = "تعديل أنواع الوثائق",
                ["DocumentType.Delete"] = "حذف أنواع الوثائق",

                // Document CRUD
                ["Document.Read"] = "قراءة الوثائق",
                ["Document.Create"] = "إنشاء وثائق جديدة",
                ["Document.Edit"] = "تعديل الوثائق",
                ["Document.Delete"] = "حذف الوثائق",

                // “BranchManager” كانت موجودة كسطر Permission عندك بس هي بالحقيقة Role
                // إذا كانت عندك أيضاً Permission بنفس الاسم:
                ["BranchManager"] = "مدير الفرع",
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
    }
}
