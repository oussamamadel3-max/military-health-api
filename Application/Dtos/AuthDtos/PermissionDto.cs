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
        /// نطاق الصلاحية المباشرة.
        /// للمنفّذ المحلي: <c>null</c> أو غياب القيمة يعني استخدام المركز النشط الذي يتحقق منه الخادم، وليس إنشاء إسناد مركزي؛ ويُرفض إن اختلفت القيمة عن المركز النشط.
        /// للمنفّذ الذي يملك صلاحية العملية نفسها بنطاق مركزي: <c>null</c> قد يعني نطاقاً مركزياً عاماً عندما تسمح العملية بذلك، والقيمة تقيّد الصلاحية بذلك المركز.
        /// </summary>
        public Guid? CenterId { get; set; }
    }

    /// <summary>
    /// مزامنة المجموعة النهائية للصلاحيات المباشرة ضمن نطاق مركز واحد أو النطاق المركزي.
    /// القائمة الفارغة تسحب الصلاحيات المباشرة التي يملك المنفّذ تفويض سحبها داخل ذلك النطاق.
    /// </summary>
    public class SyncDirectPermissionsDto
    {
        [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
        [StringLength(450, MinimumLength = 1)]
        public string UserId { get; set; } = string.Empty;

        public List<Guid> PermissionIds { get; set; } = new();

        /// <summary>
        /// نطاق المزامنة.
        /// للمنفّذ المحلي: <c>null</c> أو غياب القيمة يعني استخدام المركز النشط الذي يتحقق منه الخادم، وليس إنشاء إسناد مركزي؛ ويُرفض إن اختلفت القيمة عن المركز النشط.
        /// للمنفّذ الذي يملك صلاحية العملية نفسها بنطاق مركزي: <c>null</c> قد يعني نطاقاً مركزياً عاماً عندما تسمح العملية بذلك.
        /// </summary>
        public Guid? CenterId { get; set; }
    }

    /// <summary>صلاحية مباشرة طُلب سحبها أثناء المزامنة وبقيت لأنها خارج حدود تفويض المنفّذ.</summary>
    public sealed class SkippedDirectPermissionRevokeDto
    {
        public Guid PermissionId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>نتيجة مزامنة الصلاحيات المباشرة.</summary>
    public sealed class DirectPermissionSyncResultDto
    {
        public int Added { get; set; }
        public int Removed { get; set; }
        public bool SessionsInvalidated { get; set; }

        /// <summary>
        /// صلاحيات مباشرة ضمن نطاق المزامنة طُلبت إزالتها من المجموعة النهائية لكن لم تُسحب لحدود التفويض.
        /// قائمة فارغة عند عدم وجود عناصر متخطاة.
        /// </summary>
        public List<SkippedDirectPermissionRevokeDto> SkippedRevokes { get; set; } = new();
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

