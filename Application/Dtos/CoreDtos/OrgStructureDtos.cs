using System.ComponentModel.DataAnnotations;
using Application.Common;

namespace Application.Dtos.CoreDtos;

public class GuidDropDownDto { public Guid Id { get; set; } public string Name { get; set; } = null!; }
public class SetActiveStatusDto { public bool IsActive { get; set; } }
public class SetRecordStatusDto { [Required] public string Status { get; set; } = null!; }

public class OrgUnitTypeDto
{
    public Guid OrgUnitTypeId { get; set; }
    public string Code { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public short DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}
public class CreateOrgUnitTypeDto
{
    [Required] public string Code { get; set; } = null!;
    [Required] public string NameAr { get; set; } = null!;
    [Required] public short DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
public class UpdateOrgUnitTypeDto : CreateOrgUnitTypeDto { }

public class OrgUnitDto
{
    public Guid OrgUnitId { get; set; }
    public string? Code { get; set; }
    public string NameAr { get; set; } = null!;
    public Guid OrgUnitTypeId { get; set; }
    public string? OrgUnitTypeNameAr { get; set; }
    public Guid? ParentOrgUnitId { get; set; }
    public string? ParentNameAr { get; set; }
    public string Status { get; set; } = null!;
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
public class OrgUnitTreeNodeDto
{
    public Guid OrgUnitId { get; set; }
    public string? Code { get; set; }
    public string NameAr { get; set; } = null!;
    public Guid OrgUnitTypeId { get; set; }
    public string? OrgUnitTypeNameAr { get; set; }
    public Guid? ParentOrgUnitId { get; set; }
    public string Status { get; set; } = null!;
    public List<OrgUnitTreeNodeDto> Children { get; set; } = new();
}
public class CreateOrgUnitDto
{
    public string? Code { get; set; }
    [Required] public string NameAr { get; set; } = null!;
    [Required] public Guid OrgUnitTypeId { get; set; }
    public Guid? ParentOrgUnitId { get; set; }
    public string Status { get; set; } = CoreStatuses.Active;
}
public class UpdateOrgUnitDto : CreateOrgUnitDto { }

public class UserOrgScopeDto
{
    public Guid UserOrgScopeId { get; set; }
    public string UserId { get; set; } = null!;
    public string? UserName { get; set; }
    public string? UserFullName { get; set; }
    public Guid PermissionId { get; set; }
    public string? PermissionName { get; set; }
    public Guid OrgUnitId { get; set; }
    public string? OrgUnitNameAr { get; set; }
    public string ScopeMode { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime AssignedAt { get; set; }
    public string? AssignedByUserId { get; set; }
}
public class CreateUserOrgScopeDto
{
    [Required] public string UserId { get; set; } = null!;
    [Required] public Guid PermissionId { get; set; }
    [Required] public Guid OrgUnitId { get; set; }
    [Required] public string ScopeMode { get; set; } = ScopeModes.Subtree;
    public string Status { get; set; } = CoreStatuses.Active;
}
public class UpdateUserOrgScopeDto
{
    [Required] public Guid PermissionId { get; set; }
    [Required] public Guid OrgUnitId { get; set; }
    [Required] public string ScopeMode { get; set; } = ScopeModes.Subtree;
    public string Status { get; set; } = CoreStatuses.Active;
}
public class AccessibleOrgUnitDto
{
    public Guid OrgUnitId { get; set; }
    public string NameAr { get; set; } = null!;
    public string? Code { get; set; }
    public string ScopeMode { get; set; } = null!;
}
