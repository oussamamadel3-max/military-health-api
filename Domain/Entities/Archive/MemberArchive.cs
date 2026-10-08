using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Archive;

public class MemberArchive
{
    public Guid ArchiveId { get; set; }
    public Guid MemberId { get; set; }
    public Guid FinalDecisionId { get; set; }
    public Guid CenterId { get; set; }
    public string ArchiveNumber { get; set; } = null!;
    public DateTimeOffset ArchivedAt { get; set; }
    public Guid? DigitalCopyFileId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public FileAsset? DigitalCopyFile { get; set; }
}
