using Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos
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

    public class AssignPermissionToUserDto
    {
        [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
        [StringLength(450, MinimumLength = 1)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MinLength(1, ErrorMessage = "يجب تحديد صلاحية واحدة على الأقل.")]
        public List<Guid> PermissionId { get; set; } = new();

        /// <summary>
        /// نطاق الصلاحية المباشرة. للمدير المحلي يُؤخذ المركز النشط إن تُرك فارغاً، ويُرفض إن اختلف عنه.
        /// للجهة المركزية: القيمة الفارغة تعني صلاحية مركزية، والقيمة تقيّد الصلاحية بذلك المركز.
        /// </summary>
        public Guid? CenterId { get; set; }
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

    public class PermissionModuleItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string ActionKey { get; set; } = "";
        public string ActionLabel { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }

    public class PermissionModuleDto
    {
        public string ModuleKey { get; set; } = "";
        public string ModuleLabel { get; set; } = "";
        public List<PermissionModuleItemDto> Permissions { get; set; } = new();
    }
}

