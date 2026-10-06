namespace Domain.Entities.Auth;

public class UserPasswordHistory
{
    public Guid PasswordHistoryId { get; set; }
    public string UserId { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public Guid PasswordPolicyId { get; set; }
    public DateTimeOffset SetAt { get; set; }

    public User User { get; set; } = null!;
    public PasswordPolicy PasswordPolicy { get; set; } = null!;
}
