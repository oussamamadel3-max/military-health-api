using Domain.Common;
using Domain.Interfaces;

namespace Domain.Entities.Auth;
public class UserPermission : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PermissionId { get; set; }
    public string UserId { get; set; } = null!;
    public virtual User User { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;

    public long GetPrimaryKey() => AuditPrimaryKey.FromGuid(Id);
}
