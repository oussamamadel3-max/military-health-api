using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Domain.Entities.Auth;

public class User : IdentityUser
{
    public DateTime CreationDate { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;
    public string? FullName { get; set; }

    public IList<Document> Documents { get; set; } = new List<Document>();

    public IList<UserRoleAssociation> UserRoleAssociations { get; set; } = new List<UserRoleAssociation>();
    public IList<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
    /// <summary>
    /// صلاحيات الحقول الخاصة بالمستخدم (جدول ربط مع FieldPermission)
    /// </summary>
    public IList<UserFieldPermission> UserFieldPermissions { get; set; } = new List<UserFieldPermission>();
}
