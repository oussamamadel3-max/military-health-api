namespace Domain.Entities.Core;

public class OrgUnitType
{
    public Guid OrgUnitTypeId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public short DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
