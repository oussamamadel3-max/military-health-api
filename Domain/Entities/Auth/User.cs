using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Domain.Entities.Auth;

public class User : IdentityUser
{
    public DateTime CreationDate { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;
    public string? FullName { get; set; }

    /// <summary>
    /// مسار نسبي لصورة الملف الشخصي داخل التخزين.
    /// </summary>
    public string? ProfilePhotoPath { get; set; }

    /// <summary>
    /// مستوى تصعيد حظر تسجيل الدخول (progressive lockout). يُصفَّر عند نجاح الدخول أو بعد فترة هدوء.
    /// </summary>
    public int LoginLockoutTier { get; set; }

    public IList<Document> Documents { get; set; } = new List<Document>();

    public IList<UserRoleAssociation> UserRoleAssociations { get; set; } = new List<UserRoleAssociation>();
    public IList<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
    /// <summary>
    /// صلاحيات الحقول الخاصة بالمستخدم (جدول ربط مع FieldPermission)
    /// </summary>
    public IList<UserFieldPermission> UserFieldPermissions { get; set; } = new List<UserFieldPermission>();
}
