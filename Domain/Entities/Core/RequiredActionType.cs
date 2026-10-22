namespace Domain.Entities.Core;

public class RequiredActionType
{
    public short ActionTypeId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public bool RequiresDescription { get; set; }
    public bool RequiresTargetClinic { get; set; }
    public bool IsActive { get; set; } = true;
}
