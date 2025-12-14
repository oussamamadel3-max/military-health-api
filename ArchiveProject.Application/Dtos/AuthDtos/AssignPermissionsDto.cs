
namespace ArchiveProject.Application.Dtos.AuthDtos;
public class AssignPermissionsDto
{
    public string RoleId { get; set; }
    public List<Guid> PermissionsIds { get; set; } = new();
}


