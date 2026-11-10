using Application.Interfaces.Services;

namespace Application.Dtos.AuthDtos;

public class UserEffectiveAccessDto
{
    public string UserId { get; set; } = string.Empty;
    public bool IsSelfView { get; set; }
    public bool SetupIncomplete { get; set; }
    public Guid? ContextCenterId { get; set; }
    public List<AssignedCenterDto> ActiveCenters { get; set; } = new();
    public List<EffectiveRoleDto> Roles { get; set; } = new();
    public List<EffectiveDirectPermissionDto> DirectPermissions { get; set; } = new();
    public List<EffectivePermissionDto> EffectivePermissions { get; set; } = new();
    public List<CenterEffectivePermissionsDto> ByCenter { get; set; } = new();
}

public class EffectiveRoleDto
{
    public Guid? AssignmentId { get; set; }
    public string RoleId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string RoleNameAr { get; set; } = string.Empty;
    public Guid? CenterId { get; set; }
    public string? CenterCode { get; set; }
    public string? CenterNameAr { get; set; }
    public bool IsCentralScope { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public string? AssignedByFullName { get; set; }
}

public class EffectiveDirectPermissionDto
{
    public Guid? AssignmentId { get; set; }
    public string PermissionName { get; set; } = string.Empty;
    public string PermissionNameAr { get; set; } = string.Empty;
    public Guid? CenterId { get; set; }
    public string? CenterCode { get; set; }
    public string? CenterNameAr { get; set; }
    public bool IsCentralScope { get; set; }
    public DateTimeOffset? GrantedAt { get; set; }
    public string? GrantedByFullName { get; set; }
}

public class EffectivePermissionSourceDto
{
    public string SourceType { get; set; } = string.Empty;
    public string? RoleId { get; set; }
    public string? RoleName { get; set; }
    public string? RoleNameAr { get; set; }
}

public class EffectivePermissionDto
{
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public bool FromRole { get; set; }
    public bool FromDirect { get; set; }
    public List<EffectivePermissionSourceDto> Sources { get; set; } = new();
}

public class CenterEffectivePermissionsDto
{
    public Guid? CenterId { get; set; }
    public string? CenterCode { get; set; }
    public string? CenterNameAr { get; set; }
    public bool IsCentralContext { get; set; }
    public List<EffectivePermissionDto> Permissions { get; set; } = new();
}
