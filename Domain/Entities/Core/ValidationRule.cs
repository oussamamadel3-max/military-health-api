namespace Domain.Entities.Core;

public class ValidationRule
{
    public Guid ValidationRuleId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string TargetEntity { get; set; } = null!;
    public string TargetField { get; set; } = null!;
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public string? Unit { get; set; }
    public int VersionNo { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? UpdatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Auth.User? UpdatedByUser { get; set; }
}
