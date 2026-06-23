using Application.Attributes;
using Application.Common;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos;
public class UserDto
{
    public UserDto(string? id, string userName, string email, bool isActive, DateTime creationDate)
    {
        Id = id;
        UserName = userName;
        Email = email;
        IsActive = isActive;
        CreationDate = creationDate;
    }
    public string? Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreationDate { get; set; }
}

public class UpdateUserDto
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string? FullName { get; set; }

    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "اسم المستخدم يحوي محارف غير مسموحة")]
    public string? UserName { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(20)]
    [RegularExpression(@"^[0-9+ -]*$", ErrorMessage = "رقم الهاتف غير صالح")]
    public string? Phone { get; set; }

    public bool? IsActive { get; set; }
}
public class UpdateUserPatchRequest
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string? fullName { get; set; }

    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "اسم المستخدم يحوي محارف غير مسموحة")]
    public string? username { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string? email { get; set; }

    [StringLength(20)]
    [RegularExpression(@"^[0-9+ -]*$", ErrorMessage = "رقم الهاتف غير صالح")]
    public string? phone { get; set; }
    public bool? isActive { get; set; }
    public List<RoleLiteDto>? roles { get; set; }
}
public class UpdateUserStatusRequest
{
    public bool isActive { get; set; }
}


public class CreateUserDto
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string fullName { get; set; } = "";

    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "اسم المستخدم يحوي محارف غير مسموحة")]
    public string username { get; set; } = "";

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string email { get; set; } = "";

    [StringLength(20)]
    [RegularExpression(@"^[0-9+ -]*$", ErrorMessage = "رقم الهاتف غير صالح")]
    public string? phone { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 8)]
    [RegularExpression(PasswordPolicy.ComplexityPattern, ErrorMessage = PasswordPolicy.ComplexityErrorMessage)]
    public string password { get; set; } = "";
    public bool isActive { get; set; } = true;
    public List<string> roles { get; set; } = new();
}


public record RoleLiteDto(string RoleId, string RoleName);
public record PermissionLiteDto(string PermissionId, string PermissionName);
public class UserListItemDto
{
    public string userId { get; set; } = string.Empty;
    public string? fullName { get; set; }
    public string username { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
    public string? phone { get; set; }
    public bool isActive { get; set; }
    public DateTime createdAt { get; set; }
    public int rolesCount { get; set; }
    public int permissionsCount { get; set; }
    public List<RoleLiteDto> roles { get; set; } = new();
    public List<PermissionLiteDto> Permissions { get; set; } = new();


}
public class UpdateUserWithRolesDto
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "اسم المستخدم يحوي محارف غير مسموحة")]
    public required string UserName { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public required string Email { get; set; }

    public List<string> RoleIds { get; set; } = new List<string>();
}


public class DropdownUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
