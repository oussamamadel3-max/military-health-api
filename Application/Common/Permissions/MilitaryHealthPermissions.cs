namespace Application.Common.Permissions;

/// <summary>
/// رموز صلاحيات مجال الصحة العسكرية المستخرجة من عمليات SRS §7.5.
/// تُستخدم مع نظام صلاحيات القالب القائم عبر Permission.Name.
/// </summary>
public static class MilitaryHealthPermissions
{
    public static class Centers
    {
        public const string Manage = "Centers.Manage";
    }

    public static class UserCenters
    {
        public const string Manage = "UserCenters.Manage";
    }

    public static class UserClinics
    {
        public const string Manage = "UserClinics.Manage";
    }

    public static class Members
    {
        public const string CreateUpdate = "Members.CreateUpdate";
        public const string View = "Members.View";
        /// <summary>بحث وعرض ملف المنتسب عبر المراكز (صلاحية مركزية صريحة).</summary>
        public const string ViewCentral = "Members.ViewCentral";
        public const string PrintReceipt = "Members.PrintReceipt";
        public const string GeneralNotes = "Members.GeneralNotes";
        public const string Import = "Members.Import";
        public const string Export = "Members.Export";
        public const string Transfer = "Members.Transfer";
    }

    public static class Clinical
    {
        public const string ExamWrite = "Clinical.ExamWrite";
        public const string AssessmentWrite = "Clinical.AssessmentWrite";
        public const string DeferredAction = "Clinical.DeferredAction";
        public const string FinalDecision = "Clinical.FinalDecision";
    }

    public static class Medical
    {
        public const string ConsultationsInvestigations = "Medical.ConsultationsInvestigations";
        public const string AttachmentsRead = "Medical.AttachmentsRead";
    }

    public static class Export
    {
        public const string Recruitment = "Export.Recruitment";
        public const string ViewHistory = "Export.ViewHistory";
    }

    public static class Reports
    {
        public const string Local = "Reports.Local";
        public const string Central = "Reports.Central";
    }

    public static class Archive
    {
        public const string ViewPrint = "Archive.ViewPrint";
    }

    public static class System
    {
        public const string ReferenceData = "System.ReferenceData";
    }

    public static class AuditLog
    {
        /// <summary>موجودة في الكتالوج لأن المصفوفة تذكر العملية صراحة، وكل الأدوار «لا».</summary>
        public const string Modify = "AuditLog.Modify";
    }
}
