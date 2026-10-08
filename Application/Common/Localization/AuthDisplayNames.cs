namespace Application.Common.Localization
{
    public static class AuthDisplayNames
    {
        private static readonly Dictionary<string, string> RoleMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["SuperAdmin"] = "مدير النظام المركزي",
                ["SUPERADMIN"] = "مدير النظام المركزي",
                ["CenterManager"] = "مدير المركز",
                ["CENTERMANAGER"] = "مدير المركز",
                ["ReceptionClerk"] = "أمين الاستقبال",
                ["RECEPTIONCLERK"] = "أمين الاستقبال",
                ["ClinicDoctor"] = "طبيب العيادة",
                ["CLINICDOCTOR"] = "طبيب العيادة",
                ["Supervisor"] = "المشرف",
                ["SUPERVISOR"] = "المشرف",
                ["CenterOfficer"] = "المسؤول",
                ["CENTEROFFICER"] = "المسؤول",
                ["ArchiveClerk"] = "الديوان",
                ["ARCHIVECLERK"] = "الديوان",
                ["Auditor"] = "المدقق",
                ["AUDITOR"] = "المدقق",
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
                ["AuditLog.Modify"] = "تعديل أو حذف سجل التدقيق",

                ["Dashboard.Read"] = "قراءة لوحة التحكم",
                ["Dashboard.Export"] = "تصدير تقرير لوحة التحكم",

                ["Centers.Manage"] = "إدارة المراكز الصحية",
                ["UserCenters.Manage"] = "إسناد المستخدمين إلى المراكز",
                ["Members.CreateUpdate"] = "إنشاء ملف المنتسب وتعديله",
                ["Members.View"] = "البحث وعرض ملف المنتسب",
                ["Members.PrintReceipt"] = "طباعة الإيصال والباركود",
                ["Members.GeneralNotes"] = "إدخال الملاحظات العامة للمريض",
                ["Members.Import"] = "استيراد معلومات المرضى من Excel",
                ["Members.Export"] = "تصدير معلومات المرضى إلى Excel",
                ["Members.Transfer"] = "نقل ملف منتسب بين المراكز",
                ["Clinical.ExamWrite"] = "تسجيل فحص العيادة وتعديله",
                ["Clinical.AssessmentWrite"] = "تسجيل تقييم العيادة",
                ["Clinical.DeferredAction"] = "تحديد الإجراء المطلوب للحالة المؤجلة",
                ["Clinical.FinalDecision"] = "إصدار القرار النهائي المعتمد وتعديله",
                ["Medical.ConsultationsInvestigations"] = "تسجيل الاستشارات والاستقصاءات ورفع المرفقات",
                ["Medical.AttachmentsRead"] = "فتح المرفقات الطبية وتنزيلها",
                ["Export.Recruitment"] = "التصدير للتجنيد",
                ["Export.ViewHistory"] = "عرض سجل الملفات المصدَّرة",
                ["Reports.Local"] = "التقارير المحلية",
                ["Reports.Central"] = "التقارير المركزية والمقارنة بين المراكز",
                ["Archive.ViewPrint"] = "الأرشيف والتقرير الكامل والطباعة",
                ["System.ReferenceData"] = "إدارة القوائم المرجعية وثوابت النظام",
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
                ["Centers"] = "المراكز الصحية",
                ["UserCenters"] = "إسناد المراكز",
                ["Members"] = "ملفات المنتسبين",
                ["Clinical"] = "الفحوص السريرية",
                ["Medical"] = "الاستشارات والاستقصاءات",
                ["Export"] = "التصدير",
                ["Reports"] = "التقارير",
                ["Archive"] = "الأرشيف",
                ["System"] = "إعدادات النظام",
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
                ["Manage"] = "إدارة",
                ["CreateUpdate"] = "إنشاء وتعديل",
                ["View"] = "عرض",
                ["PrintReceipt"] = "طباعة الإيصال",
                ["GeneralNotes"] = "الملاحظات العامة",
                ["Import"] = "استيراد",
                ["Transfer"] = "نقل",
                ["ExamWrite"] = "تسجيل الفحص",
                ["AssessmentWrite"] = "تسجيل التقييم",
                ["DeferredAction"] = "الإجراء المؤجل",
                ["FinalDecision"] = "القرار النهائي",
                ["ConsultationsInvestigations"] = "الاستشارات والاستقصاءات",
                ["AttachmentsRead"] = "فتح المرفقات",
                ["Recruitment"] = "التصدير للتجنيد",
                ["ViewHistory"] = "عرض السجل",
                ["Local"] = "محلي",
                ["Central"] = "مركزي",
                ["ViewPrint"] = "عرض وطباعة",
                ["ReferenceData"] = "البيانات المرجعية",
                ["Modify"] = "تعديل",
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
