using Domain.Entities.Core;
using Domain.Entities.Patient;

namespace Domain.Entities.Export;

public class RecruitmentExportItem
{
    public Guid ExportBatchId { get; set; }
    public Guid MemberId { get; set; }
    public Guid FinalDecisionId { get; set; }
    public DateTimeOffset DecisionAtSnapshot { get; set; }
    public short ResultStatusId { get; set; }
    public string? ResultReasonSnapshot { get; set; }
    public long FileNumberSnapshot { get; set; }
    public string RecruitmentNumberSnapshot { get; set; } = null!;
    public long DataRevisionSnapshot { get; set; }
    public bool IsReexport { get; set; }
    public Guid? PreviousExportBatchId { get; set; }
    public DateTimeOffset ExportedAt { get; set; }
    public Guid RecruitmentCenterId { get; set; }
    public string RecruitmentCenterCodeSnapshot { get; set; } = null!;
    public string RecruitmentCenterNameSnapshot { get; set; } = null!;

    public RecruitmentExportBatch ExportBatch { get; set; } = null!;
    public Member Member { get; set; } = null!;
    public ResultStatus ResultStatus { get; set; } = null!;
    public RecruitmentCenter RecruitmentCenter { get; set; } = null!;
}
