using Domain.Entities.Auth;
using Domain.Entities.Core;

namespace Domain.Entities.Patient;

public class MemberGeneralNote
{
    public Guid NoteId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public string NoteText { get; set; } = null!;
    public string AuthorUserId { get; set; } = null!;
    public Guid AuthorClinicId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset WrittenAt { get; set; }
    public string UpdatedByUserId { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public Clinic AuthorClinic { get; set; } = null!;
    public User AuthorUser { get; set; } = null!;
    public User UpdatedByUser { get; set; } = null!;
}
