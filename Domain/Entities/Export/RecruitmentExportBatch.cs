using Domain.Entities.Auth;
using Domain.Entities.Core;

namespace Domain.Entities.Export;

public class RecruitmentExportBatch
{
    public Guid ExportBatchId { get; set; }
    public Guid CenterId { get; set; }
    public string CenterCodeSnapshot { get; set; } = null!;
    public string CenterNameSnapshot { get; set; } = null!;
    public string ExportedByUserId { get; set; } = null!;
    public Guid? GeneratedFileId { get; set; }
    public int RecordCount { get; set; }
    public string Status { get; set; } = "GENERATING";
    public string? FilterSnapshot { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public MedicalCenter Center { get; set; } = null!;
    public User ExportedByUser { get; set; } = null!;
    public FileAsset? GeneratedFile { get; set; }
}
