using Domain.Common;
using Domain.Entities.Auth;

namespace Domain.Entities.Core;

public class UserOrgScope : BaseEntity
{
    public Guid UserOrgScopeId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public Guid OrgUnitId { get; set; }
    public string ScopeMode { get; set; } = "SUBTREE";
    public string Status { get; set; } = "ACTIVE";
    public DateTime AssignedAt { get; set; }
    public string? AssignedByUserId { get; set; }

    public User? User { get; set; }
    public Permission Permission { get; set; } = null!;
    public OrgUnit OrgUnit { get; set; } = null!;
    public User? AssignedByUser { get; set; }
}
