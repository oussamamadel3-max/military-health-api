using Domain.Entities.Core;
using Domain.Interfaces;

namespace Domain.Entities.Auth;

/// <summary>
/// إسناد دور للمستخدم. CenterId الفارغ يعني إسناداً مركزياً عاماً.
/// AssignedByUserId إلزامي. أول مستخدم في البذر يسند الدور إلى نفسه.
/// </summary>
public class UserRoleAssociation : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public Guid? CenterId { get; set; }
    public bool IsActive { get; set; } = true;
    public string AssignedByUserId { get; set; } = null!;
    public DateTimeOffset AssignedAt { get; set; }
    public string? RevokedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }

    public virtual User User { get; set; } = null!;
    public virtual Role Role { get; set; } = null!;
    public MedicalCenter? Center { get; set; }
    public User AssignedByUser { get; set; } = null!;
    public User? RevokedByUser { get; set; }
}
