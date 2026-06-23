using Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos;
public class RoleDto
{

    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}
public class CreateRoleDto
{
    [Required(ErrorMessage = "اسم الدور مطلوب.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
    public bool? IsActive { get; set; } = true;
}

public class UpdateRoleDto
{
    [Required(ErrorMessage = "اسم الدور مطلوب.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
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
    [Required(ErrorMessage = "اسم الدور مطلوب.")]
    [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
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
