namespace Application.Dtos.Centers;

public class ExecuteHistoricalMigrationDto
{
    public Guid TargetCenterId { get; set; }
}

public class HistoricalMigrationEntityCountDto
{
    public string EntityName { get; set; } = null!;
    public string LabelAr { get; set; } = null!;
    public int OrphanCount { get; set; }
    public int AssignedCount { get; set; }
    public bool Unassignable { get; set; }
    public string? BlockReason { get; set; }
    public IReadOnlyList<Guid> SampleIds { get; set; } = [];
}

/// <summary>تقرير الجرد أو نتيجة الإسناد. الاكتمال لا يُستنتج من هذا التقرير وحده.</summary>
public class HistoricalMigrationReportDto
{
    public Guid? TargetCenterId { get; set; }
    public int TotalOrphanCount { get; set; }
    public int TotalUnassignableCount { get; set; }
    public int TotalAssignedCount { get; set; }
    public bool MigrationVerified { get; set; }
    public IReadOnlyList<HistoricalMigrationEntityCountDto> Entities { get; set; } = [];
}

public class HistoricalMigrationCompletionDto
{
    public Guid Id { get; set; }
    public Guid TargetCenterId { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
    public string VerifiedByUserId { get; set; } = null!;
    public string SummaryJson { get; set; } = "[]";
    public int ActiveCenterCountAtVerification { get; set; }
    public bool PreExistingMultiCenterDetected { get; set; }
}

public class HistoricalMigrationStatusDto
{
    public bool MigrationVerified { get; set; }
    public int ActiveCenterCount { get; set; }
    public bool PreExistingMultiCenterWithoutVerification { get; set; }
    public HistoricalMigrationCompletionDto? Completion { get; set; }
}
