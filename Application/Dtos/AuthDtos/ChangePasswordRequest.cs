using Application.Attributes;
using Application.Common;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos;

public class ChangePasswordRequest
{
    [Required(ErrorMessage = "كلمة المرور الحالية مطلوبة.")]
    [StringLength(PasswordPolicy.MaxLength, MinimumLength = 1)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة.")]
    [StringLength(PasswordPolicy.MaxLength, MinimumLength = PasswordPolicy.MinLength,
        ErrorMessage = "كلمة المرور الجديدة يجب أن تكون بين 8 و128 محرفاً.")]
    [RegularExpression(PasswordPolicy.ComplexityPattern, ErrorMessage = PasswordPolicy.ComplexityErrorMessage)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "تأكيد كلمة المرور مطلوب.")]
    [Compare(nameof(NewPassword), ErrorMessage = PasswordPolicy.ConfirmationMismatchMessage)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AdminChangePasswordRequest
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة.")]
    [StringLength(PasswordPolicy.MaxLength, MinimumLength = PasswordPolicy.MinLength,
        ErrorMessage = "كلمة المرور الجديدة يجب أن تكون بين 8 و128 محرفاً.")]
    [RegularExpression(PasswordPolicy.ComplexityPattern, ErrorMessage = PasswordPolicy.ComplexityErrorMessage)]
    public string NewPassword { get; set; } = string.Empty;
}
