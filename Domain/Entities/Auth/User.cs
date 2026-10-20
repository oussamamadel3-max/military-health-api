using Domain.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Domain.Entities.Auth;

public class User : IdentityUser, IAuditableEntity
{
    public DateTime CreationDate { get; set; } = DateTime.Now;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? FullName { get; set; }

    /// <summary>
    /// عند true يُمنع الوصول التشغيلي حتى يغيّر المستخدم كلمة مروره.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// مستوى تصعيد حظر تسجيل الدخول (progressive lockout). يُصفَّر عند نجاح الدخول أو بعد فترة هدوء.
    /// </summary>
    public int LoginLockoutTier { get; set; }

    public IList<UserRoleAssociation> UserRoleAssociations { get; set; } = new List<UserRoleAssociation>();
    public IList<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
    /// <summary>
    /// صلاحيات الحقول الخاصة بالمستخدم (جدول ربط مع FieldPermission)
    /// </summary>
    public IList<UserFieldPermission> UserFieldPermissions { get; set; } = new List<UserFieldPermission>();
    public UserProfile? Profile { get; set; }
    public IList<UserContact> Contacts { get; set; } = new List<UserContact>();
}
