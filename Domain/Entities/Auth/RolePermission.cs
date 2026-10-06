using Domain.Interfaces;

namespace Domain.Entities.Auth;

/// <summary>
/// يبقى المفتاح Guid الخاص بالقالب لأن الخدمات الحالية تعتمد عليه.
/// حقول المنح والإلغاء تغطي دورة حياة إسناد الصلاحية للدور.
/// </summary>
public class RolePermission : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RoleId { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public bool IsActive { get; set; } = true;
    public string GrantedByUserId { get; set; } = null!;
    public DateTimeOffset GrantedAt { get; set; }
    public string? RevokedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }

    public virtual Role Role { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;
    public User GrantedByUser { get; set; } = null!;
    public User? RevokedByUser { get; set; }
}
