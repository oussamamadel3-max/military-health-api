using Application.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Application.Dtos.AuthDtos;

public class AssignPermissionsDto
{
    [Required(ErrorMessage = "معرف الدور مطلوب.")]
    [StringLength(450, MinimumLength = 1)]
    public string RoleId { get; set; }
    public List<Guid> PermissionsIds { get; set; } = new();
}
