using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Medical;

public class Consultation
{
    public Guid ConsultationId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid ClinicId { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public string? ConsultantUserId { get; set; }
    public string? ConsultantName { get; set; }
    public short ConsultationTypeId { get; set; }
    public string ReferralReason { get; set; } = null!;
    public string Status { get; set; } = "PENDING";
    public string? ResultText { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResultedAt { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public Clinic Clinic { get; set; } = null!;
    public ConsultationType ConsultationType { get; set; } = null!;
    public User RequestedByUser { get; set; } = null!;
    public User? ConsultantUser { get; set; }
}
