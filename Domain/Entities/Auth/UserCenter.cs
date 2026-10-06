using Domain.Entities.Core;
using Domain.Interfaces;

namespace Domain.Entities.Auth;

public class UserCenter : IAuditableEntity
{
    public string UserId { get; set; } = null!;
    public Guid CenterId { get; set; }
    public bool IsActive { get; set; } = true;
    public string AssignedByUserId { get; set; } = null!;
    public DateTimeOffset AssignedAt { get; set; }
    public string? RevokedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }

    public User User { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public User AssignedByUser { get; set; } = null!;
    public User? RevokedByUser { get; set; }
}
