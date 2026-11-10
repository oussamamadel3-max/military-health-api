using Application.Attributes;
using Application.Common;
using Application.Interfaces.Services;
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

    [Required(ErrorMessage = "الرقم الوظيفي مطلوب.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "الرقم الوظيفي يجب أن يكون بين 1 و 50 حرفاً.")]
    public string? EmployeeNumber { get; set; }

    [Required(ErrorMessage = "الرتبة أو المسمى الوظيفي مطلوب.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "الرتبة أو المسمى الوظيفي يجب أن يكون بين 1 و 100 حرف.")]
    public string? RankOrTitle { get; set; }

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

    [Required(ErrorMessage = "الرقم الوظيفي مطلوب.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "الرقم الوظيفي يجب أن يكون بين 1 و 50 حرفاً.")]
    public string? employeeNumber { get; set; }

    [Required(ErrorMessage = "الرتبة أو المسمى الوظيفي مطلوب.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "الرتبة أو المسمى الوظيفي يجب أن يكون بين 1 و 100 حرف.")]
    public string? rankOrTitle { get; set; }

    public bool? isActive { get; set; }

    /// <summary>
    /// لم يعد هذا الحقل يغيّر الإسنادات. أي قيمة، بما فيها القائمة الفارغة، تُرفض بـ 422.
    /// </summary>
    public List<RoleLiteDto>? roles { get; set; }

    /// <summary>
    /// لم يعد هذا الحقل يغيّر إسناد المركز. أي قيمة تُرفض بـ 422.
    /// </summary>
    public Guid? centerId { get; set; }
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

    [Required(ErrorMessage = "الرقم الوظيفي مطلوب.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "الرقم الوظيفي يجب أن يكون بين 1 و 50 حرفاً.")]
    public string employeeNumber { get; set; } = "";

    [Required(ErrorMessage = "الرتبة أو المسمى الوظيفي مطلوب.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "الرتبة أو المسمى الوظيفي يجب أن يكون بين 1 و 100 حرف.")]
    public string rankOrTitle { get; set; } = "";

    [Required]
    [StringLength(PasswordPolicy.MaxLength, MinimumLength = PasswordPolicy.MinLength)]
    [RegularExpression(PasswordPolicy.ComplexityPattern, ErrorMessage = PasswordPolicy.ComplexityErrorMessage)]
    public string password { get; set; } = "";

    [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب.")]
    [Compare(nameof(password), ErrorMessage = PasswordPolicy.ConfirmationMismatchMessage)]
    public string confirmPassword { get; set; } = "";

    public bool isActive { get; set; } = true;

    /// <summary>افتراضي true: إلزام تغيير كلمة المرور عند أول دخول.</summary>
    public bool forcePasswordChange { get; set; } = true;

    /// <summary>
    /// مركز الإسناد. لمدير المركز يُؤخذ المركز النشط إن تُرك فارغاً، ويُرفض إن اختلف عنه.
    /// </summary>
    public Guid? centerId { get; set; }

    [Required(ErrorMessage = "يجب اختيار دور واحد على الأقل.")]
    [MinLength(1, ErrorMessage = "يجب اختيار دور واحد على الأقل.")]
    public List<string> roles { get; set; } = new();
}

/// <summary>استجابة إنشاء مستخدم بدون كلمة المرور.</summary>
public class CreatedUserResponseDto
{
    public string fullName { get; set; } = "";
    public string username { get; set; } = "";
    public string email { get; set; } = "";
    public string? phone { get; set; }
    public string employeeNumber { get; set; } = "";
    public string rankOrTitle { get; set; } = "";
    public bool isActive { get; set; }
    public bool mustChangePassword { get; set; }
    public Guid? centerId { get; set; }
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
    public string? employeeNumber { get; set; }
    public string? rankOrTitle { get; set; }
    public bool isActive { get; set; }
    public bool mustChangePassword { get; set; }
    public DateTime createdAt { get; set; }
    public DateTimeOffset? lastLoginAt { get; set; }
    public DateTimeOffset? updatedAt { get; set; }
    public int rolesCount { get; set; }
    public int permissionsCount { get; set; }
    public List<RoleLiteDto> roles { get; set; } = new();
    public List<PermissionLiteDto> Permissions { get; set; } = new();
    /// <summary>المراكز الفعالة المسندة للمستخدم.</summary>
    public List<AssignedCenterDto> centers { get; set; } = new();
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

    public List<string>? RoleIds { get; set; }

    public Guid? CenterId { get; set; }
}


public class DropdownUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
