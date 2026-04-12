using ArchiveProject.Application.Attributes;
using System.ComponentModel.DataAnnotations;

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
        [Required(ErrorMessage = "اسم الصلاحية مطلوب.")]
        [StringLength(200, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
    }

    public class UpdatePermissionDto
    {
        [Required(ErrorMessage = "اسم الصلاحية مطلوب.")]
        [StringLength(200, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }
    }

    public class AssignPermissionToUserDto
    {
        [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
        [StringLength(450, MinimumLength = 1)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MinLength(1, ErrorMessage = "يجب تحديد صلاحية واحدة على الأقل.")]
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
        [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
        [StringLength(450, MinimumLength = 1)]
        public string UserId { get; set; } = null!;

        public List<string>? RoleIds { get; set; }

        public List<string>? PermissionIds { get; set; }
    }
}

