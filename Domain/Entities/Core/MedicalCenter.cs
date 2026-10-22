namespace Domain.Entities.Core;

public class MedicalCenter
{
    public Guid CenterId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public string? LocationText { get; set; }
    public string? ParentOrgText { get; set; }
    public string? ContactInfo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? DeactivationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
