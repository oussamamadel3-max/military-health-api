using ArchiveProject.Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace ArchiveProject.Application.Dtos.AuthDtos;

public class ChangePasswordRequest
{
    [Required(ErrorMessage = "كلمة المرور الحالية مطلوبة.")]
    [StringLength(128, MinimumLength = 1)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "كلمة المرور الجديدة يجب ألا تقل عن 8 أحرف.")]
    public string NewPassword { get; set; } = string.Empty;
}

public class AdminChangePasswordRequest
{
    [Required(ErrorMessage = "معرف المستخدم مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "كلمة المرور الجديدة يجب ألا تقل عن 8 أحرف.")]
    public string NewPassword { get; set; } = string.Empty;
}
