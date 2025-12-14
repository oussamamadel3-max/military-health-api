namespace ArchiveProject.Application.Dtos.AuthDtos;
public class AssignRolesDto
{
    public string UserId { get; set; } = null!;
    public List<string> RoleIds { get; set; } = new List<string>();
    public List<string>? RoleNames { get; set; } = new List<string>();
}

public class RemoveRoleDto
{
    public string UserId { get; set; } = null!;
    public string RoleId { get; set; } = string.Empty;
}

public class RemovePermissionsDto
{
    public string UserId { get; set; } = null!;
    public List<Guid> PermissionIds { get; set; } = new();
}

public class RemoveFieldPermissionsDto
{
    public string UserId { get; set; } = null!;
    public List<Guid> FieldPermissionIds { get; set; } = new();
}
