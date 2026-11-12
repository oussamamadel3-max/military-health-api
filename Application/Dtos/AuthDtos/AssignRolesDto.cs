using Application.Common.Security;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos;

/// <summary>
/// Scoped role sync for a user. RoleIds and RoleNames are merged and de-duplicated
/// (case-insensitive). At least one valid non-blank token is required; empty sync
/// is rejected — clearing a center's last roles uses the explicit revoke path with
/// <see cref="MinimumActiveRoleGuard"/>.
/// </summary>
public class AssignRolesDto : IValidatableObject
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = null!;

    /// <summary>
    /// Role identifiers to keep in the sync scope. Merged with <see cref="RoleNames"/>.
    /// Must yield at least one non-blank token together with RoleNames.
    /// </summary>
    public List<string>? RoleIds { get; set; }

    /// <summary>
    /// Role names to keep in the sync scope. Merged with <see cref="RoleIds"/>.
    /// Whitespace-only entries are ignored; if nothing valid remains, validation fails.
    /// </summary>
    public List<string>? RoleNames { get; set; }

    /// <summary>
    /// نطاق مزامنة الأدوار.
    /// للمنفّذ المحلي: <c>null</c> أو غياب القيمة يعني استخدام المركز النشط الذي يتحقق منه الخادم، وليس إنشاء إسناد مركزي؛ ويُرفض إن اختلفت القيمة عن المركز النشط.
    /// للمنفّذ الذي يملك صلاحية العملية نفسها بنطاق مركزي: <c>null</c> قد يعني نطاقاً مركزياً عاماً عندما تكون الأدوار مركزية وتسمح العملية بذلك؛ وإلا قد يُطلب مركز صريح للأدوار المقيدة.
    /// </summary>
    public Guid? CenterId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinimumActiveRoleGuard.CollectTokens(RoleIds, RoleNames).Count == 0)
        {
            yield return new ValidationResult(
                MinimumActiveRoleGuard.AtLeastOneRoleMessage,
                new[] { nameof(RoleIds), nameof(RoleNames) });
        }
    }
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
