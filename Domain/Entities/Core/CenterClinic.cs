using Domain.Interfaces;

namespace Domain.Entities.Core;

public class CenterClinic : IAuditableEntity
{
    public Guid CenterClinicId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }

    public MedicalCenter Center { get; set; } = null!;
    public Clinic Clinic { get; set; } = null!;
}
