namespace ArchiveProject.Application.Dtos.AuthDtos
{
    public class PermissionDto
    {
        public PermissionDto() { }
        public PermissionDto(Guid id, string name, string? description, bool isActive)
        {
            Id = id;
            Name = name;
            Description = description;
            IsActive = isActive;
        }

        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }
    public class CreatePermissionDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
    }
    public class UpdatePermissionDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
    public class AssignPermissionToUserDto
    {
        public string UserId { get; set; } = string.Empty;
        public List<Guid> PermissionId { get; set; } = new();
    }


    public class PermissionWithRolesDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
        public List<string> AssignedRoleIds { get; set; } = new List<string>();
        public int AssignedRolesCount { get; set; }
    }

    public class PermissionWithRolesResultDto
    {
        public List<RoleDto> Roles { get; set; } = new List<RoleDto>();
        public List<PermissionWithRolesDto> Permissions { get; set; } = new List<PermissionWithRolesDto>();
    }

    public class AssignRolesAndPermissionsDto
    {
        public string UserId { get; set; } = null!;

        public List<string>? RoleIds { get; set; }

        public List<string>? PermissionIds { get; set; }
    }
}

