namespace ArchiveProject.Application.Dtos.AuthDtos;
public class RoleDto
{

    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}
public class CreateRoleDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? IsActive { get; set; } = true;
}
public class UpdateRoleDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class RoleDetailsDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public int PermissionsCount { get; set; }
    public List<PermissionInfoDto> Permissions { get; set; } = new List<PermissionInfoDto>();
    public int AssignedUsersCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }  // ????? ?????? ??????
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }  // ????? ?????? ???????
}

public class CreateRoleWithPermissionsDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Guid> PermissionIds { get; set; } = new();
    public bool? IsActive { get; set; } = true;
}


public class DropdownRoleDto
{
    public string roleId { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
}

public class RoleStatusUpdateDto
{
    public bool IsActive { get; set; }
}

public class PermissionInfoDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
