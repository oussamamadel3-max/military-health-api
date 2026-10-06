using Domain.Entities.Core;
using Domain.Interfaces;

namespace Domain.Entities.Auth;

public class UserClinicAssignment : IAuditableEntity
{
    public Guid AssignmentId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string? SpecialtyTitle { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset AssignedAt { get; set; }
    public string AssignedByUserId { get; set; } = null!;
    public string? RevokedByUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }

    public User User { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public Clinic Clinic { get; set; } = null!;
    public User AssignedByUser { get; set; } = null!;
    public User? RevokedByUser { get; set; }
}
