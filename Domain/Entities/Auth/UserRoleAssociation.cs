namespace Domain.Entities.Auth;
public class UserRoleAssociation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public virtual User User { get; set; } = null!;
    public virtual Role Role { get; set; } = null!;
}
