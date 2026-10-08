using Domain.Entities.Auth;
using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Medical;

public class MedicalAttachment
{
    public Guid AttachmentId { get; set; }
    public Guid MemberId { get; set; }
    public Guid CenterId { get; set; }
    public Guid FileAssetId { get; set; }
    public Guid? ConsultationId { get; set; }
    public Guid? InvestigationId { get; set; }
    public string UploadedByUserId { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public Member Member { get; set; } = null!;
    public MedicalCenter Center { get; set; } = null!;
    public FileAsset FileAsset { get; set; } = null!;
    public User UploadedByUser { get; set; } = null!;
}
