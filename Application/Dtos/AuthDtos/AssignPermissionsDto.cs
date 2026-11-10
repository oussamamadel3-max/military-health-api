using Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos;

public class AssignPermissionsDto
{
    [Required(ErrorMessage = "معرف الدور مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string RoleId { get; set; }

    [Required(ErrorMessage = "يجب اختيار صلاحية واحدة على الأقل.")]
    [MinLength(1, ErrorMessage = "يجب اختيار صلاحية واحدة على الأقل.")]
    public List<Guid> PermissionsIds { get; set; } = new();

    /// <summary>
    /// الإصدار الذي قرأه العميل لمجموعة صلاحيات الدور. الاختلاف عن القيمة الحالية يُرفض بتعارض.
    /// </summary>
    public long PermissionSetVersion { get; set; }
}

/// <summary>نتيجة مزامنة صلاحيات الدور بعد نجاح الحفظ.</summary>
public sealed class RolePermissionSyncResultDto
{
    public string Message { get; set; } = string.Empty;
    public long PermissionSetVersion { get; set; }
    public int Added { get; set; }
    public int Removed { get; set; }
}
