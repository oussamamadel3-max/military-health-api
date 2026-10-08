using Domain.Entities.Core;
using Domain.Interfaces;

namespace Domain.Entities.Auth;

/// <summary>
/// صلاحية مباشرة. CenterId الفارغ يعني صلاحية مركزية.
/// </summary>
public class UserPermission : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PermissionId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid? CenterId { get; set; }
    public bool IsActive { get; set; } = true;
    public string GrantedByUserId { get; set; } = null!;
    public DateTimeOffset GrantedAt { get; set; }
    public string? RevokedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }

    public virtual User User { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;
    public MedicalCenter? Center { get; set; }
    public User GrantedByUser { get; set; } = null!;
    public User? RevokedByUser { get; set; }
}
