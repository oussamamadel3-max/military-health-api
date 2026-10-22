using Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos;

public class AssignRolesDto
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    /// <summary>
    /// قد تكون فارغة لمزامنة نهائية بلا أدوار (مع منطق الحماية في الخدمة).
    /// </summary>
    public List<string> RoleIds { get; set; } = new List<string>();

    public List<string>? RoleNames { get; set; } = new List<string>();
}

public class RemoveRoleDto
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    [Required(ErrorMessage = "معرف الدور مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string RoleId { get; set; } = string.Empty;
}

public class RemovePermissionsDto
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    [Required]
    [MinLength(1, ErrorMessage = "يجب تحديد صلاحية واحدة على الأقل.")]
    public List<Guid> PermissionIds { get; set; } = new();
}

public class RemoveFieldPermissionsDto
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    [Required]
    [MinLength(1, ErrorMessage = "يجب تحديد صلاحية حقل واحدة على الأقل.")]
    public List<Guid> FieldPermissionIds { get; set; } = new();
}
