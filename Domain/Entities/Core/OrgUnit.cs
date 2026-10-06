using Domain.Common;

namespace Domain.Entities.Core;

public class OrgUnit : BaseEntity
{
    public Guid OrgUnitId { get; set; }
    public string? Code { get; set; }
    public string NameAr { get; set; } = null!;
    public Guid OrgUnitTypeId { get; set; }
    public Guid? ParentOrgUnitId { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime? ArchivedAt { get; set; }

    public OrgUnitType OrgUnitType { get; set; } = null!;
    public OrgUnit? ParentOrgUnit { get; set; }
}
