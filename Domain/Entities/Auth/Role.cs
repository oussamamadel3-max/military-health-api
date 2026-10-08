using Domain.Common;
using Domain.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Domain.Entities.Auth;
public class Role : IdentityRole, IAuditableEntity
{
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public IList<UserRoleAssociation>? UserRoleAssociations { get; set; }
    public IList<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    /// <summary>
    /// صلاحيات الحقول المرتبطة بهذا الدور (جدول ربط مع FieldPermission)
    /// </summary>
    public IList<RoleFieldPermission> RoleFieldPermissions { get; set; } = new List<RoleFieldPermission>();

    public long GetPrimaryKey() => AuditPrimaryKey.FromString(Id);
}
