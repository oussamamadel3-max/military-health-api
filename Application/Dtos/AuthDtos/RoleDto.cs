using Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos;
public class RoleDto
{

    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
    /// <summary>النطاق التشغيلي: Center = مقيد بمركز، Central = مركزي.</summary>
    public RoleScope Scope { get; set; } = RoleScope.Center;
    /// <summary>دور أساسي من كتالوج SRS §6.2 (الثمانية).</summary>
    public bool IsSystemRole { get; set; }
    /// <summary>إصدار مجموعة الصلاحيات المطلوب إرساله عند المزامنة.</summary>
    public long PermissionSetVersion { get; set; }
}
public class CreateRoleDto
{
    [Required(ErrorMessage = "اسم الدور مطلوب.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "نطاق الدور مطلوب.")]
    [EnumDataType(typeof(RoleScope), ErrorMessage = "نطاق الدور غير صالح. القيم: Center أو Central.")]
    public RoleScope Scope { get; set; }

    public bool? IsActive { get; set; } = true;
}

public class UpdateRoleDto
{
    [Required(ErrorMessage = "اسم الدور مطلوب.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "نطاق الدور مطلوب.")]
    [EnumDataType(typeof(RoleScope), ErrorMessage = "نطاق الدور غير صالح. القيم: Center أو Central.")]
    public RoleScope Scope { get; set; }
}

public class RoleDetailsDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    /// <summary>النطاق التشغيلي: Center = مقيد بمركز، Central = مركزي.</summary>
    public RoleScope Scope { get; set; }
    /// <summary>دور أساسي من كتالوج SRS §6.2 (الثمانية).</summary>
    public bool IsSystemRole { get; set; }
    public int PermissionsCount { get; set; }
    public List<PermissionInfoDto> Permissions { get; set; } = new List<PermissionInfoDto>();
    /// <summary>إصدار مجموعة الصلاحيات المطلوب إرساله عند المزامنة.</summary>
    public long PermissionSetVersion { get; set; }
    public int AssignedUsersCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }
}

public class CreateRoleWithPermissionsDto
{
    [Required(ErrorMessage = "اسم الدور مطلوب.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "نطاق الدور مطلوب.")]
    [EnumDataType(typeof(RoleScope), ErrorMessage = "نطاق الدور غير صالح. القيم: Center أو Central.")]
    public RoleScope Scope { get; set; }

    [Required(ErrorMessage = "يجب اختيار صلاحية واحدة على الأقل.")]
    [MinLength(1, ErrorMessage = "يجب اختيار صلاحية واحدة على الأقل.")]
    public List<Guid> PermissionIds { get; set; } = new();
    public bool? IsActive { get; set; } = true;
}


public class DropdownRoleDto
{
    public string roleId { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public RoleScope scope { get; set; }
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
