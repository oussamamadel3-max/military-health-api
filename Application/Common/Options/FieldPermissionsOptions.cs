namespace Application.Common.Options;

/// <summary>
/// إعدادات إنفاذ صلاحيات الحقول على مسارات الأعمال.
/// تعطيل <see cref="EnforcementEnabled"/> يترك كتالوج الإدارة وCRUD فعّالين دون إخفاء/رفض على Members.
/// </summary>
public sealed class FieldPermissionsOptions
{
    public const string SectionName = "FieldPermissions";

    /// <summary>
    /// عند <c>false</c> (الافتراضي) لا يُطبَّق Default Deny ولا فحوصات الكتابة على الحقول المحمية.
    /// فعّل بعد مراجعة إسنادات الأدوار/المستخدمين في البيئة المستهدفة.
    /// </summary>
    public bool EnforcementEnabled { get; set; }
}
