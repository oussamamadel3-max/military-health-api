namespace ArchiveProject.Domain.Entities.Auth;
public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public IList<RolePermission>? RolePermissions { get; set; }
    public IList<UserPermission>? UserPermissions { get; set; }
}
