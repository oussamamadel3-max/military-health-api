namespace Application.Common.Permissions;

/// <summary>
/// كتالوج تأسيس الأدوار والصلاحيات وروابطها المستخرجة صراحة من SRS §6.2 و§7.5.
/// لا يشمل إسنادات المستخدمين أو المراكز.
/// </summary>
public static class MilitaryHealthSeedCatalog
{
    public const string SourceRoles = "SRS §6.2 المستخدمون";
    public const string SourceMatrix = "SRS §7.5 مصفوفة الصلاحيات";
    public const string SourceScope = "SRS §7.5 نطاق تطبيق الصلاحيات الممنوحة";

    /// <summary>أدوار الأعمال من SRS. اسم Identity لمدير النظام المركزي يبقى SuperAdmin كما في القالب وAuthDisplayNames.</summary>
    public static readonly SeedRole[] Roles =
    [
        new("SuperAdmin", "SUPERADMIN", "مدير النظام المركزي", IsCentralCapable: true),
        new("CenterManager", "CENTERMANAGER", "مدير المركز", IsCentralCapable: false),
        new("ReceptionClerk", "RECEPTIONCLERK", "أمين الاستقبال", IsCentralCapable: false),
        new("ClinicDoctor", "CLINICDOCTOR", "طبيب العيادة", IsCentralCapable: false),
        new("Supervisor", "SUPERVISOR", "المشرف", IsCentralCapable: false),
        new("CenterOfficer", "CENTEROFFICER", "المسؤول", IsCentralCapable: false),
        new("ArchiveClerk", "ARCHIVECLERK", "الديوان", IsCentralCapable: false),
        new("Auditor", "AUDITOR", "المدقق", IsCentralCapable: true),
    ];

    /// <summary>
    /// صلاحيات مجال الصحة العسكرية المذكورة كعمليات في المصفوفة، برموز تقنية ثابتة مشتقة من اسم العملية دون إضافة عمليات غير مذكورة.
    /// </summary>
    public static readonly SeedPermission[] DomainPermissions =
    [
        new(MilitaryHealthPermissions.Centers.Manage, "إدارة المراكز الصحية", "Centers.Manage"),
        new(MilitaryHealthPermissions.UserCenters.Manage, "إسناد المستخدمين إلى المراكز", "UserCenters.Manage"),
        new(MilitaryHealthPermissions.UserClinics.Manage, "إسناد الأطباء إلى عيادات المراكز", "UserClinics.Manage"),
        new(MilitaryHealthPermissions.Members.CreateUpdate, "إنشاء ملف المنتسب وتعديله", "Members.CreateUpdate"),
        new(MilitaryHealthPermissions.Members.View, "البحث وعرض ملف المنتسب", "Members.View"),
        new(MilitaryHealthPermissions.Members.PrintReceipt, "طباعة الإيصال والباركود", "Members.PrintReceipt"),
        new(MilitaryHealthPermissions.Members.GeneralNotes, "إدخال الملاحظات العامة للمريض", "Members.GeneralNotes"),
        new(MilitaryHealthPermissions.Members.Import, "استيراد معلومات المرضى من Excel", "Members.Import"),
        new(MilitaryHealthPermissions.Members.Export, "تصدير معلومات المرضى إلى Excel", "Members.Export"),
        new(MilitaryHealthPermissions.Members.Transfer, "نقل ملف منتسب بين المراكز", "Members.Transfer"),
        new(MilitaryHealthPermissions.Clinical.ExamWrite, "تسجيل فحص العيادة وتعديله", "Clinical.ExamWrite"),
        new(MilitaryHealthPermissions.Clinical.AssessmentWrite, "تسجيل تقييم العيادة", "Clinical.AssessmentWrite"),
        new(MilitaryHealthPermissions.Clinical.DeferredAction, "تحديد الإجراء المطلوب للحالة المؤجلة", "Clinical.DeferredAction"),
        new(MilitaryHealthPermissions.Clinical.FinalDecision, "إصدار القرار النهائي المعتمد وتعديله", "Clinical.FinalDecision"),
        new(MilitaryHealthPermissions.Medical.ConsultationsInvestigations, "تسجيل الاستشارات والاستقصاءات ورفع المرفقات", "Medical.ConsultationsInvestigations"),
        new(MilitaryHealthPermissions.Medical.AttachmentsRead, "فتح المرفقات الطبية وتنزيلها", "Medical.AttachmentsRead"),
        new(MilitaryHealthPermissions.Export.Recruitment, "التصدير للتجنيد", "Export.Recruitment"),
        new(MilitaryHealthPermissions.Export.ViewHistory, "عرض سجل الملفات المصدَّرة", "Export.ViewHistory"),
        new(MilitaryHealthPermissions.Reports.Local, "التقارير المحلية", "Reports.Local"),
        new(MilitaryHealthPermissions.Reports.Central, "التقارير المركزية والمقارنة بين المراكز", "Reports.Central"),
        new(MilitaryHealthPermissions.Archive.ViewPrint, "الأرشيف والتقرير الكامل والطباعة", "Archive.ViewPrint"),
        new(MilitaryHealthPermissions.System.ReferenceData, "إدارة القوائم المرجعية وثوابت النظام", "System.ReferenceData"),
        new(MilitaryHealthPermissions.AuditLog.Modify, "تعديل أو حذف سجل التدقيق", "AuditLog.Modify"),
    ];

    /// <summary>صلاحيات القالب التي تبقى في الكتالوج التشغيلي الحالي ولا تُستبدل.</summary>
    public static readonly SeedPermission[] TemplateIamPermissions =
    [
        new(SystemPermissions.UserManagement.Read, "قراءة بيانات المستخدمين", "UserManagement.Read"),
        new(SystemPermissions.UserManagement.ViewEffectiveAccess, "عرض الصلاحيات الفعلية للمستخدم", "UserManagement.ViewEffectiveAccess"),
        new(SystemPermissions.UserManagement.Create, "إنشاء مستخدمين جدد", "UserManagement.Create"),
        new(SystemPermissions.UserManagement.Edit, "تعديل بيانات المستخدمين", "UserManagement.Edit"),
        new(SystemPermissions.UserManagement.Delete, "حذف المستخدمين", "UserManagement.Delete"),
        new(SystemPermissions.UserManagement.ChangeStatus, "تغيير حالة المستخدمين", "UserManagement.ChangeStatus"),
        new(SystemPermissions.UserManagement.AssignRoles, "إسناد أدوار للمستخدمين", "UserManagement.AssignRoles"),
        new(SystemPermissions.UserManagement.AssignPermissions, "إسناد صلاحيات مباشرة للمستخدمين", "UserManagement.AssignPermissions"),
        new(SystemPermissions.UserManagement.ResetPassword, "إعادة تعيين كلمة مرور مستخدم", "UserManagement.ResetPassword"),
        new(SystemPermissions.UserManagement.RemoveFieldPermissions, "إزالة صلاحيات الحقول عن مستخدم", "UserManagement.RemoveFieldPermissions"),
        new(SystemPermissions.RoleManagement.Read, "قراءة الأدوار", "RoleManagement.Read"),
        new(SystemPermissions.RoleManagement.Create, "إنشاء أدوار", "RoleManagement.Create"),
        new(SystemPermissions.RoleManagement.Edit, "تعديل بيانات الأدوار", "RoleManagement.Edit"),
        new(SystemPermissions.RoleManagement.Delete, "حذف الأدوار", "RoleManagement.Delete"),
        new(SystemPermissions.RoleManagement.ChangeStatus, "تغيير حالة الأدوار", "RoleManagement.ChangeStatus"),
        new(SystemPermissions.RoleManagement.AssignPermissions, "إسناد صلاحيات للأدوار", "RoleManagement.AssignPermissions"),
        new(SystemPermissions.PermissionManagement.Read, "قراءة تعريفات الصلاحيات", "PermissionManagement.Read"),
        new(SystemPermissions.PermissionManagement.ChangeStatus, "تغيير حالة الصلاحيات", "PermissionManagement.ChangeStatus"),
        new(SystemPermissions.FieldPermissionManagement.Read, "قراءة صلاحيات الحقول", "FieldPermissionManagement.Read"),
        new(SystemPermissions.FieldPermissionManagement.Create, "إنشاء صلاحيات حقول", "FieldPermissionManagement.Create"),
        new(SystemPermissions.FieldPermissionManagement.Edit, "تعديل صلاحيات الحقول", "FieldPermissionManagement.Edit"),
        new(SystemPermissions.FieldPermissionManagement.Delete, "حذف صلاحيات الحقول", "FieldPermissionManagement.Delete"),
        new(SystemPermissions.FieldPermissionManagement.ChangeAccessType, "تغيير نوع وصول صلاحية الحقل", "FieldPermissionManagement.ChangeAccessType"),
        new(SystemPermissions.AuditLog.Read, "قراءة سجلات التدقيق", "AuditLog.Read"),
        new(SystemPermissions.AuditLog.Export, "تصدير تقرير السجل", "AuditLog.Export"),
        new(SystemPermissions.Dashboard.Read, "قراءة لوحة التحكم", "Dashboard.Read"),
        new(SystemPermissions.Dashboard.Export, "تصدير تقرير لوحة التحكم", "Dashboard.Export"),
    ];

    public static IEnumerable<SeedPermission> AllCatalogPermissions =>
        TemplateIamPermissions.Concat(DomainPermissions);

    /// <summary>
    /// روابط الدور←الصلاحية من عمود «نعم» في SRS §7.5 فقط.
    /// الصفوف التي تتوافق مع صلاحيات القالب فُكّكت إلى رموزها الحالية دون منح صلاحيات حقول القالب من المصفوفة.
    /// </summary>
    public static readonly SeedRolePermissionGrant[] MatrixGrants =
    [
        // إدارة المراكز الصحية
        Grant("SuperAdmin", MilitaryHealthPermissions.Centers.Manage),

        // إدارة المستخدمين (دون إسناد الأدوار/الصلاحيات/إعادة التعيين — لها صفوف مستقلة)
        Grant("SuperAdmin",
            SystemPermissions.UserManagement.Read,
            SystemPermissions.UserManagement.Create,
            SystemPermissions.UserManagement.Edit,
            SystemPermissions.UserManagement.Delete,
            SystemPermissions.UserManagement.ChangeStatus),
        Grant("CenterManager",
            SystemPermissions.UserManagement.Read,
            SystemPermissions.UserManagement.Create,
            SystemPermissions.UserManagement.Edit,
            SystemPermissions.UserManagement.Delete,
            SystemPermissions.UserManagement.ChangeStatus),

        // إدارة الأدوار وكتالوج الصلاحيات (مركزي فقط في نطاق التطبيق)
        Grant("SuperAdmin",
            SystemPermissions.RoleManagement.Read,
            SystemPermissions.RoleManagement.Create,
            SystemPermissions.RoleManagement.Edit,
            SystemPermissions.RoleManagement.Delete,
            SystemPermissions.RoleManagement.ChangeStatus,
            SystemPermissions.RoleManagement.AssignPermissions,
            SystemPermissions.PermissionManagement.Read,
            SystemPermissions.PermissionManagement.ChangeStatus),

        // إسناد الأدوار والمراكز للمستخدمين، وإسناد الأطباء إلى العيادات (FR-020)
        Grant("SuperAdmin",
            SystemPermissions.UserManagement.AssignRoles,
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.UserCenters.Manage,
            MilitaryHealthPermissions.UserClinics.Manage),
        Grant("CenterManager",
            SystemPermissions.UserManagement.AssignRoles,
            SystemPermissions.UserManagement.AssignPermissions,
            MilitaryHealthPermissions.UserCenters.Manage,
            MilitaryHealthPermissions.UserClinics.Manage),

        // إعادة تعيين كلمة مرور مستخدم
        Grant("SuperAdmin", SystemPermissions.UserManagement.ResetPassword),
        Grant("CenterManager", SystemPermissions.UserManagement.ResetPassword),

        // إنشاء ملف المنتسب وتعديله
        Grant("ReceptionClerk", MilitaryHealthPermissions.Members.CreateUpdate),
        Grant("Supervisor", MilitaryHealthPermissions.Members.CreateUpdate),

        // البحث وعرض ملف المنتسب
        Grant("SuperAdmin", MilitaryHealthPermissions.Members.View),
        Grant("CenterManager", MilitaryHealthPermissions.Members.View),
        Grant("ReceptionClerk", MilitaryHealthPermissions.Members.View),
        Grant("ClinicDoctor", MilitaryHealthPermissions.Members.View),
        Grant("Supervisor", MilitaryHealthPermissions.Members.View),
        Grant("CenterOfficer", MilitaryHealthPermissions.Members.View),
        Grant("ArchiveClerk", MilitaryHealthPermissions.Members.View),
        Grant("Auditor", MilitaryHealthPermissions.Members.View),

        // طباعة الإيصال والباركود
        Grant("ReceptionClerk", MilitaryHealthPermissions.Members.PrintReceipt),

        // تسجيل فحص العيادة وتعديله
        Grant("ClinicDoctor", MilitaryHealthPermissions.Clinical.ExamWrite),

        // تسجيل تقييم العيادة
        Grant("ClinicDoctor", MilitaryHealthPermissions.Clinical.AssessmentWrite),

        // تحديد الإجراء المطلوب للحالة المؤجلة
        Grant("ClinicDoctor", MilitaryHealthPermissions.Clinical.DeferredAction),
        Grant("Supervisor", MilitaryHealthPermissions.Clinical.DeferredAction),

        // إدخال الملاحظات العامة للمريض
        Grant("ClinicDoctor", MilitaryHealthPermissions.Members.GeneralNotes),
        Grant("Supervisor", MilitaryHealthPermissions.Members.GeneralNotes),

        // تسجيل الاستشارات والاستقصاءات ورفع المرفقات
        Grant("ClinicDoctor", MilitaryHealthPermissions.Medical.ConsultationsInvestigations),

        // فتح المرفقات الطبية وتنزيلها
        Grant("ClinicDoctor", MilitaryHealthPermissions.Medical.AttachmentsRead),
        Grant("Supervisor", MilitaryHealthPermissions.Medical.AttachmentsRead),
        Grant("ArchiveClerk", MilitaryHealthPermissions.Medical.AttachmentsRead),

        // إصدار القرار النهائي المعتمد وتعديله
        Grant("Supervisor", MilitaryHealthPermissions.Clinical.FinalDecision),

        // التصدير للتجنيد
        Grant("Supervisor", MilitaryHealthPermissions.Export.Recruitment),

        // عرض سجل الملفات المصدَّرة
        Grant("SuperAdmin", MilitaryHealthPermissions.Export.ViewHistory),
        Grant("CenterManager", MilitaryHealthPermissions.Export.ViewHistory),
        Grant("Supervisor", MilitaryHealthPermissions.Export.ViewHistory),
        Grant("CenterOfficer", MilitaryHealthPermissions.Export.ViewHistory),
        Grant("Auditor", MilitaryHealthPermissions.Export.ViewHistory),

        // استيراد معلومات المرضى من Excel
        Grant("SuperAdmin", MilitaryHealthPermissions.Members.Import),
        Grant("CenterManager", MilitaryHealthPermissions.Members.Import),
        Grant("ReceptionClerk", MilitaryHealthPermissions.Members.Import),
        Grant("Supervisor", MilitaryHealthPermissions.Members.Import),

        // تصدير معلومات المرضى إلى Excel
        Grant("SuperAdmin", MilitaryHealthPermissions.Members.Export),
        Grant("CenterManager", MilitaryHealthPermissions.Members.Export),
        Grant("ReceptionClerk", MilitaryHealthPermissions.Members.Export),
        Grant("Supervisor", MilitaryHealthPermissions.Members.Export),
        Grant("CenterOfficer", MilitaryHealthPermissions.Members.Export),

        // لوحة المؤشرات والقائمة النهائية → Dashboard القائم
        Grant("SuperAdmin", SystemPermissions.Dashboard.Read, SystemPermissions.Dashboard.Export),
        Grant("CenterManager", SystemPermissions.Dashboard.Read, SystemPermissions.Dashboard.Export),
        Grant("Supervisor", SystemPermissions.Dashboard.Read, SystemPermissions.Dashboard.Export),
        Grant("CenterOfficer", SystemPermissions.Dashboard.Read, SystemPermissions.Dashboard.Export),
        Grant("Auditor", SystemPermissions.Dashboard.Read, SystemPermissions.Dashboard.Export),

        // التقارير المحلية
        Grant("SuperAdmin", MilitaryHealthPermissions.Reports.Local),
        Grant("CenterManager", MilitaryHealthPermissions.Reports.Local),
        Grant("Supervisor", MilitaryHealthPermissions.Reports.Local),
        Grant("CenterOfficer", MilitaryHealthPermissions.Reports.Local),
        Grant("Auditor", MilitaryHealthPermissions.Reports.Local),

        // التقارير المركزية والمقارنة بين المراكز
        Grant("SuperAdmin", MilitaryHealthPermissions.Reports.Central),
        Grant("CenterOfficer", MilitaryHealthPermissions.Reports.Central),
        Grant("Auditor", MilitaryHealthPermissions.Reports.Central),

        // الأرشيف والتقرير الكامل والطباعة
        Grant("SuperAdmin", MilitaryHealthPermissions.Archive.ViewPrint),
        Grant("CenterManager", MilitaryHealthPermissions.Archive.ViewPrint),
        Grant("ClinicDoctor", MilitaryHealthPermissions.Archive.ViewPrint),
        Grant("Supervisor", MilitaryHealthPermissions.Archive.ViewPrint),
        Grant("ArchiveClerk", MilitaryHealthPermissions.Archive.ViewPrint),
        Grant("Auditor", MilitaryHealthPermissions.Archive.ViewPrint),

        // نقل ملف منتسب بين المراكز
        Grant("SuperAdmin", MilitaryHealthPermissions.Members.Transfer),
        Grant("CenterManager", MilitaryHealthPermissions.Members.Transfer),

        // إدارة القوائم المرجعية وثوابت النظام
        Grant("SuperAdmin", MilitaryHealthPermissions.System.ReferenceData),

        // الاطلاع على سجل التدقيق واستخراجه
        Grant("SuperAdmin", SystemPermissions.AuditLog.Read, SystemPermissions.AuditLog.Export),
        Grant("Auditor", SystemPermissions.AuditLog.Read, SystemPermissions.AuditLog.Export),

        // عرض الصلاحيات الفعلية (FR-021) دون إدارة المستخدمين
        Grant("Auditor", SystemPermissions.UserManagement.ViewEffectiveAccess),
    ];

    /// <summary>
    /// صلاحيات القالب التي تبقى مسندة لـ SuperAdmin عبر البذر التشغيلي الحالي،
    /// بما فيها ما ليس في مصفوفة SRS (صلاحيات الحقول).
    /// </summary>
    public static readonly (string Name, string? Desc)[] TemplateBootstrapPermissions =
        TemplateIamPermissions.Select(p => (p.Name, (string?)p.DescriptionAr)).ToArray();

    public static IReadOnlyDictionary<string, string[]> GrantsByRoleName()
    {
        return MatrixGrants
            .GroupBy(g => g.RoleName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(x => x.PermissionNames).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }

    private static SeedRolePermissionGrant Grant(string roleName, params string[] permissionNames) =>
        new(roleName, permissionNames);

    public sealed record SeedRole(string Name, string NormalizedName, string DescriptionAr, bool IsCentralCapable);

    public sealed record SeedPermission(string Name, string DescriptionAr, string SourceCode);

    public sealed record SeedRolePermissionGrant(string RoleName, string[] PermissionNames);
}
