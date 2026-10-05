using Domain.Common;
using Domain.Interfaces;

namespace Domain.Entities.Auth;
public class Permission : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public IList<RolePermission>? RolePermissions { get; set; }
    public IList<UserPermission>? UserPermissions { get; set; }

    public long GetPrimaryKey() => AuditPrimaryKey.FromGuid(Id);
}
