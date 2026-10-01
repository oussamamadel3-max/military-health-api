using Domain.Common;
using Domain.Interfaces;

namespace Domain.Entities.Auth;
public class RolePermission : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RoleId { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public virtual Role Role { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;

    public long GetPrimaryKey() => AuditPrimaryKey.FromGuid(Id);
}
